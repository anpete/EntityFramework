﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Microsoft.EntityFrameworkCore.Query.ResultOperators.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public partial class IncludeCompiler
    {
        private static readonly MethodInfo _queryBufferStartTrackingMethodInfo
            = typeof(IQueryBuffer).GetTypeInfo()
                .GetDeclaredMethods(nameof(IQueryBuffer.StartTracking))
                .Single(mi => mi.GetParameters()[1].ParameterType == typeof(IEntityType));

        private static readonly ParameterExpression _includedParameter
            = Expression.Parameter(typeof(object[]), name: "included");

        private static readonly ParameterExpression _cancellationTokenParameter
            = Expression.Parameter(typeof(CancellationToken), name: "ct");

        private readonly QueryCompilationContext _queryCompilationContext;
        private readonly IQuerySourceTracingExpressionVisitorFactory _querySourceTracingExpressionVisitorFactory;

        private int _collectionIncludeId;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IncludeCompiler(
            [NotNull] QueryCompilationContext queryCompilationContext,
            [NotNull] IQuerySourceTracingExpressionVisitorFactory querySourceTracingExpressionVisitorFactory)
        {
            _queryCompilationContext = queryCompilationContext;
            _querySourceTracingExpressionVisitorFactory = querySourceTracingExpressionVisitorFactory;
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void CompileIncludes(
            [NotNull] QueryModel queryModel,
            [NotNull] ICollection<IncludeResultOperator> includeResultOperators,
            bool trackingQuery,
            bool asyncQuery)
        {
            if (queryModel.GetOutputDataInfo() is StreamedScalarValueInfo)
            {
                return;
            }

            foreach (var includeLoadTree
                in CreateIncludeLoadTrees(includeResultOperators, queryModel.SelectClause.Selector))
            {
                includeLoadTree.Compile(
                    _queryCompilationContext,
                    queryModel,
                    trackingQuery,
                    asyncQuery,
                    ref _collectionIncludeId);
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void RewriteCollectionQueries([NotNull] QueryModel queryModel)
        {
            var collectionQueryModelRewritingExpressionVisitor
                = new CollectionQueryModelRewritingExpressionVisitor(_queryCompilationContext, queryModel, this);

            queryModel.TransformExpressions(collectionQueryModelRewritingExpressionVisitor.Visit);
            
            ApplyParentOrderings(queryModel, collectionQueryModelRewritingExpressionVisitor.ParentOrderings);
        }

        private IEnumerable<IncludeLoadTree> CreateIncludeLoadTrees(
            ICollection<IncludeResultOperator> includeResultOperators,
            Expression targetExpression)
        {
            var querySourceTracingExpressionVisitor
                = _querySourceTracingExpressionVisitorFactory.Create();

            var includeLoadTrees = new List<IncludeLoadTree>();

            foreach (var includeResultOperator in includeResultOperators.ToArray())
            {
                var navigationPath = includeResultOperator.GetNavigationPath(_queryCompilationContext);

                var querySourceReferenceExpression
                    = querySourceTracingExpressionVisitor
                        .FindResultQuerySourceReferenceExpression(
                            targetExpression,
                            includeResultOperator.QuerySource);

                if (querySourceReferenceExpression == null)
                {
                    _queryCompilationContext.Logger
                        .LogWarning(
                            CoreEventId.IncludeIgnoredWarning,
                            () => CoreStrings.LogIgnoredInclude(
                                $"{includeResultOperator.QuerySource.ItemName}.{navigationPath.Select(n => n.Name).Join(".")}"));

                    continue;
                }

                var sequenceType = querySourceReferenceExpression.Type.TryGetSequenceType();

                if (sequenceType != null
                    && (_queryCompilationContext.Model.FindEntityType(sequenceType) != null
                        || _queryCompilationContext.Model.IsDelegatedIdentityEntityType(sequenceType)))
                {
                    //continue;
                }

                var includeLoadTree
                    = includeLoadTrees
                        .SingleOrDefault(
                            t => ReferenceEquals(
                                t.QuerySourceReferenceExpression, querySourceReferenceExpression));

                if (includeLoadTree == null)
                {
                    includeLoadTrees.Add(includeLoadTree = new IncludeLoadTree(querySourceReferenceExpression));
                }

                includeLoadTree.AddLoadPath(navigationPath);

                _queryCompilationContext.Logger
                    .LogDebug(
                        CoreEventId.IncludingNavigation,
                        () => CoreStrings.LogIncludingNavigation(
                            $"{includeResultOperator.PathFromQuerySource}.{includeResultOperator.NavigationPropertyPaths.Join(".")}"));

                // TODO: Hack until new Include fully implemented
                includeResultOperators.Remove(includeResultOperator);
            }

            return includeLoadTrees;
        }

        private static void ApplyParentOrderings(
            QueryModel queryModel,
            IReadOnlyCollection<Ordering> parentOrderings)
        {
            if (parentOrderings.Any())
            {
                var orderByClause
                    = queryModel.BodyClauses
                        .OfType<OrderByClause>()
                        .LastOrDefault();

                if (orderByClause == null)
                {
                    orderByClause = new OrderByClause();
                    queryModel.BodyClauses.Add(orderByClause);
                }

                foreach (var ordering in parentOrderings)
                {
                    orderByClause.Orderings.Add(ordering);
                }
            }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public static bool IsIncludeMethod([NotNull] MethodCallExpression methodCallExpression)
            => methodCallExpression.Method.MethodIsClosedFormOf(_includeMethodInfo)
               || methodCallExpression.Method.MethodIsClosedFormOf(_includeAsyncMethodInfo);

        private static readonly MethodInfo _includeMethodInfo
            = typeof(IncludeCompiler).GetTypeInfo()
                .GetDeclaredMethod(nameof(_Include));

        // ReSharper disable once InconsistentNaming
        private static TEntity _Include<TEntity>(
            QueryContext queryContext,
            TEntity entity,
            object[] included,
            Action<QueryContext, TEntity, object[]> fixup)
        {
            if (entity != null)
            {
                fixup(queryContext, entity, included);
            }

            return entity;
        }

        private static readonly MethodInfo _includeAsyncMethodInfo
            = typeof(IncludeCompiler).GetTypeInfo()
                .GetDeclaredMethod(nameof(_IncludeAsync));

        // ReSharper disable once InconsistentNaming
        private static async Task<TEntity> _IncludeAsync<TEntity>(
            QueryContext queryContext,
            TEntity entity,
            object[] included,
            Func<QueryContext, TEntity, object[], CancellationToken, Task> fixup,
            CancellationToken cancellationToken)
        {
            if (entity != null)
            {
                await fixup(queryContext, entity, included, cancellationToken);
            }

            return entity;
        }
    }
}
