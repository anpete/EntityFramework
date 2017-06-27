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
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors;
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
        
        private readonly List<QuerySourceReferenceExpression> _trackedQsres = new List<QuerySourceReferenceExpression>();

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

            var outputExpression = queryModel.GetOutputExpression();

            var includeLoadTrees = new List<IncludeLoadTree>();

            foreach (var includeResultOperator in _includeResultOperators.ToArray())
            {
                var navigationPath = includeResultOperator.GetNavigationPath(_queryCompilationContext);

                var querySourceReferenceExpression
                    = querySourceTracingExpressionVisitor
                        .FindResultQuerySourceReferenceExpression(
                            outputExpression,
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
                    = new EntityResultFindingExpressionVisitor(_queryCompilationContext);

                foreach (var querySourceReferenceExpression
                    in entityResultFindingQueryModelVisitor.FindEntitiesInResult(outputExpression)
                        .Where(qsre => !_trackedQsres.Contains(qsre)))
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

                    _trackedQsres.Add(querySourceReferenceExpression);
                }
            }

            return includeLoadTrees;
        }

        private sealed class EntityResultFindingExpressionVisitor : ExpressionVisitorBase
        {
            private readonly QueryCompilationContext _queryCompilationContext;

            private List<QuerySourceReferenceExpression> _querySourceReferenceExpressions;

            /// <summary>
            ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            public EntityResultFindingExpressionVisitor(QueryCompilationContext queryCompilationContext)
            {
                _queryCompilationContext = queryCompilationContext;
            }

            public IEnumerable<QuerySourceReferenceExpression> FindEntitiesInResult([NotNull] Expression expression)
            {
                _querySourceReferenceExpressions = new List<QuerySourceReferenceExpression>();

                Visit(expression);

                return _querySourceReferenceExpressions;
            }

            /// <summary>
            ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            protected override Expression VisitQuerySourceReference(QuerySourceReferenceExpression querySourceReferenceExpression)
            {
                var sequenceType = querySourceReferenceExpression.Type.TryGetSequenceType();

                var entityType
                    = _queryCompilationContext.FindEntityType(querySourceReferenceExpression.ReferencedQuerySource)
                      ?? ((sequenceType != null
                              ? _queryCompilationContext.Model.FindEntityType(sequenceType)
                              : null)
                          ?? _queryCompilationContext.Model.FindEntityType(querySourceReferenceExpression.Type));

                if (entityType != null)
                {
                    _querySourceReferenceExpressions.Add(querySourceReferenceExpression);
                }

                return querySourceReferenceExpression;
            }

            /// <summary>
            ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            protected override Expression VisitNew(NewExpression expression)
                => expression.Members == null ? expression : base.VisitNew(expression);

            // Prune these nodes...
            protected override Expression VisitSubQuery(SubQueryExpression expression) => expression;
            protected override Expression VisitMember(MemberExpression node) => node;
            protected override Expression VisitMethodCall(MethodCallExpression node) => node;
            protected override Expression VisitConditional(ConditionalExpression node) => node;
            protected override Expression VisitBinary(BinaryExpression node) => node;
            protected override Expression VisitTypeBinary(TypeBinaryExpression node) => node;
            protected override Expression VisitLambda<T>(Expression<T> node) => node;
            protected override Expression VisitInvocation(InvocationExpression node) => node;
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
