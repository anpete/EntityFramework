// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Microsoft.EntityFrameworkCore.Query.ResultOperators.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Clauses.StreamedData;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public partial class IncludeCompiler
    {
        private static readonly MethodInfo _referenceEqualsMethodInfo
            = typeof(object).GetTypeInfo()
                .GetDeclaredMethod(nameof(ReferenceEquals));

        private static readonly MethodInfo _collectionAccessorAddMethodInfo
            = typeof(IClrCollectionAccessor).GetTypeInfo()
                .GetDeclaredMethod(nameof(IClrCollectionAccessor.Add));

        private static readonly MethodInfo _queryBufferStartTrackingMethodInfo
            = typeof(IQueryBuffer).GetTypeInfo()
                .GetDeclaredMethods(nameof(IQueryBuffer.StartTracking))
                .Single(mi => mi.GetParameters()[1].ParameterType == typeof(IEntityType));

        private static readonly MethodInfo _queryBufferIncludeCollectionMethodInfo
            = typeof(IQueryBuffer).GetTypeInfo()
                .GetDeclaredMethod(nameof(IQueryBuffer.IncludeCollection));

        private static readonly MethodInfo _queryBufferIncludeCollectionAsyncMethodInfo
            = typeof(IQueryBuffer).GetTypeInfo()
                .GetDeclaredMethod(nameof(IQueryBuffer.IncludeCollectionAsync));

        private static readonly ParameterExpression _includedParameter
            = Expression.Parameter(typeof(object[]), name: "included");

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        private static readonly ParameterExpression _cancellationTokenParameter
            = Expression.Parameter(typeof(CancellationToken), name: "ct");

        private readonly QueryCompilationContext _queryCompilationContext;
        private readonly IQuerySourceTracingExpressionVisitorFactory _querySourceTracingExpressionVisitorFactory;
        private readonly NavigationRewritingExpressionVisitor _navigationRewritingExpressionVisitor;

        private int _collectionIncludeId;

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public IncludeCompiler(
            [NotNull] QueryCompilationContext queryCompilationContext,
            [NotNull] IQuerySourceTracingExpressionVisitorFactory querySourceTracingExpressionVisitorFactory,
            [NotNull] NavigationRewritingExpressionVisitor navigationRewritingExpressionVisitor)
        {
            _queryCompilationContext = queryCompilationContext;
            _querySourceTracingExpressionVisitorFactory = querySourceTracingExpressionVisitorFactory;
            _navigationRewritingExpressionVisitor = navigationRewritingExpressionVisitor;
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

            var includeLoadTrees
                = CreateIncludeLoadTrees(includeResultOperators, queryModel.SelectClause.Selector);

            CompileIncludes(queryModel, trackingQuery, asyncQuery, includeLoadTrees);
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
                var entityType
                    = _queryCompilationContext.Model
                        .FindEntityType(includeResultOperator.PathFromQuerySource.Type);

                var nextEntityType = entityType;

                var parts = includeResultOperator.NavigationPropertyPaths.ToArray();
                var navigationPath = new INavigation[parts.Length];

                for (var i = 0; i < parts.Length; i++)
                {
                    navigationPath[i] = nextEntityType.FindNavigation(parts[i]);

                    if (navigationPath[i] == null)
                    {
                        throw new InvalidOperationException(
                            CoreStrings.IncludeBadNavigation(parts[i], nextEntityType.DisplayName()));
                    }

                    nextEntityType = navigationPath[i].GetTargetType();
                }

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
                    && _queryCompilationContext.Model.FindEntityType(sequenceType) != null)
                {
                    continue;
                }

                if (!(!navigationPath.Any(n => n.IsCollection())
                      || navigationPath.Length == 1))
                {
                    continue;
                }

                if (navigationPath.Count(n => n.IsCollection()) > 1)
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
                    includeLoadTrees
                        .Add(includeLoadTree
                            = new IncludeLoadTree(querySourceReferenceExpression));
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

        private void CompileIncludes(
            QueryModel queryModel, 
            bool trackingQuery, 
            bool asyncQuery, 
            IEnumerable<IncludeLoadTree> includeLoadTrees)
        {
            var parentOrderings = new List<Ordering>();

            var compiledIncludes
                = (from includeLoadTree in includeLoadTrees
                   let includeExpression
                   = includeLoadTree.Compile(
                       _queryCompilationContext,
                       _navigationRewritingExpressionVisitor,
                       queryModel,
                       trackingQuery,
                       asyncQuery,
                       parentOrderings,
                       ref _collectionIncludeId)
                   select (includeLoadTree.QuerySourceReferenceExpression, includeExpression))
                .ToList();

            ApplyParentOrderings(queryModel, parentOrderings);
            ApplyIncludeExpressionsToQueryModel(queryModel, compiledIncludes);
        }

        private static void ApplyParentOrderings(
            QueryModel queryModel, IReadOnlyCollection<Ordering> parentOrderings)
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

        private static void ApplyIncludeExpressionsToQueryModel(
            QueryModel queryModel,
            IEnumerable<(
                QuerySourceReferenceExpression querySourceReferenceExpression,
                Expression expression)> compiledIncludes)
        {
            var includeReplacingExpressionVisitor = new IncludeReplacingExpressionVisitor();

            foreach (var include in compiledIncludes)
            {
                queryModel.SelectClause.TransformExpressions(
                    e => includeReplacingExpressionVisitor.Replace(
                        include.querySourceReferenceExpression,
                        include.expression,
                        e));

                foreach (var groupResultOperator
                    in queryModel.ResultOperators.OfType<GroupResultOperator>())
                {
                    groupResultOperator.ElementSelector
                        = includeReplacingExpressionVisitor.Replace(
                            include.querySourceReferenceExpression,
                            include.expression,
                            groupResultOperator.ElementSelector);
                }
            }
        }

        private static readonly MethodInfo _awaitIncludesMethodInfo
            = typeof(IncludeCompiler).GetTypeInfo()
                .GetDeclaredMethod(nameof(_AwaitIncludes));

        // ReSharper disable once InconsistentNaming
        private static async Task _AwaitIncludes(IReadOnlyList<Func<Task>> taskFactories)
        {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < taskFactories.Count; i++)
            {
                await taskFactories[i]();
            }
        }

        private class IncludeReplacingExpressionVisitor : RelinqExpressionVisitor
        {
            private QuerySourceReferenceExpression _querySourceReferenceExpression;
            private Expression _includeExpression;

            public Expression Replace(
                QuerySourceReferenceExpression querySourceReferenceExpression,
                Expression includeExpression,
                Expression searchedExpression)
            {
                _querySourceReferenceExpression = querySourceReferenceExpression;
                _includeExpression = includeExpression;

                return Visit(searchedExpression);
            }

            protected override Expression VisitQuerySourceReference(
                QuerySourceReferenceExpression querySourceReferenceExpression)
            {
                if (ReferenceEquals(querySourceReferenceExpression, _querySourceReferenceExpression))
                {
                    _querySourceReferenceExpression = null;

                    return _includeExpression;
                }

                return querySourceReferenceExpression;
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

        private static readonly MethodInfo _setRelationshipSnapshotValueMethodInfo
            = typeof(IncludeCompiler).GetTypeInfo()
                .GetDeclaredMethod(nameof(SetRelationshipSnapshotValue));

        private static void SetRelationshipSnapshotValue(
            IStateManager stateManager,
            IPropertyBase navigation,
            object entity,
            object value)
        {
            var internalEntityEntry = stateManager.TryGetEntry(entity);

            Debug.Assert(internalEntityEntry != null);

            internalEntityEntry.SetRelationshipSnapshotValue(navigation, value);
        }

        private static readonly MethodInfo _addToCollectionSnapshotMethodInfo
            = typeof(IncludeCompiler).GetTypeInfo()
                .GetDeclaredMethod(nameof(AddToCollectionSnapshot));

        private static void AddToCollectionSnapshot(
            IStateManager stateManager,
            IPropertyBase navigation,
            object entity,
            object value)
        {
            var internalEntityEntry = stateManager.TryGetEntry(entity);

            Debug.Assert(internalEntityEntry != null);

            internalEntityEntry.AddToCollectionSnapshot(navigation, value);
        }
    }
}
