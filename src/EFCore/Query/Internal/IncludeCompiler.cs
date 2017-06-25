// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Microsoft.EntityFrameworkCore.Query.ResultOperators.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
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
        private readonly List<IncludeResultOperator> _includeResultOperators;

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

            _includeResultOperators
                = _queryCompilationContext.QueryAnnotations
                    .OfType<IncludeResultOperator>()
                    .ToList();
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void CompileIncludes(
            [NotNull] QueryModel queryModel,
            bool trackingQuery,
            bool asyncQuery)
        {
            if (queryModel.GetOutputDataInfo() is StreamedScalarValueInfo)
            {
                return;
            }

            foreach (var includeLoadTree in CreateIncludeLoadTrees(queryModel, trackingQuery))
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

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void LogIgnoredIncludes()
        {
            foreach (var includeResultOperator in _includeResultOperators)
            {
                _queryCompilationContext.Logger.IncludeIgnoredWarning(includeResultOperator);
            }
        }

        private IEnumerable<IncludeLoadTree> CreateIncludeLoadTrees(QueryModel queryModel, bool trackingQuery)
        {
            var querySourceTracingExpressionVisitor
                = _querySourceTracingExpressionVisitorFactory.Create();

            var includeLoadTrees = new List<IncludeLoadTree>();

            foreach (var includeResultOperator in _includeResultOperators.ToArray())
            {
                var navigationPath = includeResultOperator.GetNavigationPath(_queryCompilationContext);

                var querySourceReferenceExpression
                    = querySourceTracingExpressionVisitor
                        .FindResultQuerySourceReferenceExpression(
                            queryModel.GetOutputExpression(),
                            includeResultOperator.QuerySource);

                if (querySourceReferenceExpression == null
                    || navigationPath == null)
                {
                    continue;
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

                _queryCompilationContext.Logger.NavigationIncluded(includeResultOperator);

                _includeResultOperators.Remove(includeResultOperator);
            }

            if (trackingQuery)
            {
                var entityResultFindingQueryModelVisitor
                    = new EntityResultFindingQueryModelVisitor(
                        _queryCompilationContext, querySourceTracingExpressionVisitor);

                entityResultFindingQueryModelVisitor.VisitQueryModel(queryModel);

                foreach (var querySourceReferenceExpression in entityResultFindingQueryModelVisitor.EntityResultExpressions)
                {
                    var includeLoadTree
                        = includeLoadTrees
                            .SingleOrDefault(
                                t => ReferenceEquals(
                                    t.QuerySourceReferenceExpression, querySourceReferenceExpression));

                    if (includeLoadTree == null)
                    {
                        includeLoadTrees.Add(new IncludeLoadTree(querySourceReferenceExpression));
                    }
                }
            }

            return includeLoadTrees;
        }

        private class EntityResultFindingQueryModelVisitor : QueryModelVisitorBase
        {
            private readonly QueryCompilationContext _queryCompilationContext;
            private readonly QuerySourceTracingExpressionVisitor _querySourceTracingExpressionVisitor;

            private readonly List<QuerySourceReferenceExpression> _entityResultExpressions = new List<QuerySourceReferenceExpression>();

            public EntityResultFindingQueryModelVisitor(
                QueryCompilationContext queryCompilationContext,
                QuerySourceTracingExpressionVisitor querySourceTracingExpressionVisitor)
            {
                _queryCompilationContext = queryCompilationContext;
                _querySourceTracingExpressionVisitor = querySourceTracingExpressionVisitor;
            }

            public IEnumerable<QuerySourceReferenceExpression> EntityResultExpressions => _entityResultExpressions;

            public override void VisitMainFromClause(MainFromClause fromClause, QueryModel queryModel)
            {
                AddEntityResultExpression(new QuerySourceReferenceExpression(fromClause), queryModel);

                base.VisitMainFromClause(fromClause, queryModel);
            }

            protected override void VisitBodyClauses(ObservableCollection<IBodyClause> bodyClauses, QueryModel queryModel)
            {
                foreach (var querySource in bodyClauses.OfType<IQuerySource>())
                {
                    AddEntityResultExpression(new QuerySourceReferenceExpression(querySource), queryModel);
                }

                base.VisitBodyClauses(bodyClauses, queryModel);
            }

            private void AddEntityResultExpression(QuerySourceReferenceExpression querySourceReferenceExpression, QueryModel queryModel)
            {
                var resultQuerySourceReferenceExpression
                    = _querySourceTracingExpressionVisitor
                        .FindResultQuerySourceReferenceExpression(
                            queryModel.GetOutputExpression(),
                            querySourceReferenceExpression.ReferencedQuerySource);

                if (resultQuerySourceReferenceExpression != null)
                {
                    var entityType = _queryCompilationContext.Model.FindEntityType(querySourceReferenceExpression.Type);

                    if (entityType != null)
                    {
                        _entityResultExpressions.Add(resultQuerySourceReferenceExpression);
                    }
                }
            }
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
