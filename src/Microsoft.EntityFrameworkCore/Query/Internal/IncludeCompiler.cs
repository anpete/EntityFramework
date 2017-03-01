// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
using Remotion.Linq.Clauses.ExpressionVisitors;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    /// <summary>
    ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
    ///     directly from your code. This API may change or be removed in future releases.
    /// </summary>
    public class IncludeCompiler
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

        private static readonly ParameterExpression _includedParameter
            = Expression.Parameter(typeof(object[]), name: "included");

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

        private struct IncludeSpecification
        {
            public IncludeSpecification(
                IncludeResultOperator includeResultOperator,
                QuerySourceReferenceExpression querySourceReferenceExpression,
                INavigation[] navigationPath)
            {
                IncludeResultOperator = includeResultOperator;
                QuerySourceReferenceExpression = querySourceReferenceExpression;
                NavigationPath = navigationPath;
            }

            public IncludeResultOperator IncludeResultOperator { get; }
            public QuerySourceReferenceExpression QuerySourceReferenceExpression { get; }
            public INavigation[] NavigationPath { get; }
        }

        /// <summary>
        ///     This API supports the Entity Framework Core infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public virtual void CompileIncludes(
            [NotNull] QueryModel queryModel,
            [NotNull] ICollection<IncludeResultOperator> includeResultOperators,
            bool trackingQuery)
        {
            var includeGroupings
                = CreateIncludeSpecifications(queryModel, includeResultOperators)
                    .GroupBy(a => a.QuerySourceReferenceExpression);

            foreach (var includeGrouping in includeGroupings)
            {
                var entityParameter = Expression.Parameter(includeGrouping.Key.Type, name: "entity");

                var propertyExpressions = new List<Expression>();
                var blockExpressions = new List<Expression>();

                if (trackingQuery)
                {
                    blockExpressions.Add(
                        Expression.Call(
                            Expression.Property(
                                EntityQueryModelVisitor.QueryContextParameter,
                                propertyName: "QueryBuffer"),
                            _queryBufferStartTrackingMethodInfo,
                            entityParameter,
                            Expression.Constant(
                                _queryCompilationContext.Model
                                    .FindEntityType(entityParameter.Type))));
                }

                var includedIndex = 0;

                foreach (var includeSpecification in includeGrouping)
                {
                    _queryCompilationContext.Logger
                        .LogDebug(
                            CoreEventId.IncludingNavigation,
                            () => CoreStrings.LogIncludingNavigation(includeSpecification.IncludeResultOperator));

                    var navigation = includeSpecification.NavigationPath[0];

                    if (navigation.IsCollection())
                    {
                        var collectionIncludeQueryModel
                            = BuildCollectionIncludeQueryModel(
                                queryModel,
                                navigation,
                                includeSpecification.QuerySourceReferenceExpression);

                        propertyExpressions.Add(
                            Expression.Lambda<Func<IEnumerable<object>>>(
                                new SubQueryExpression(collectionIncludeQueryModel)));

                        blockExpressions.Add(
                            BuildCollectionIncludeExpressions(
                                navigation,
                                entityParameter,
                                trackingQuery,
                                ref includedIndex));
                    }
                    else
                    {
                        propertyExpressions.AddRange(
                            includeSpecification.NavigationPath
                                .Select(
                                    (t, i) =>
                                        includeSpecification.NavigationPath
                                            .Take(i + 1)
                                            .Aggregate(
                                                (Expression)includeSpecification.QuerySourceReferenceExpression,
                                                EntityQueryModelVisitor.CreatePropertyExpression)));

                        blockExpressions.Add(
                            BuildIncludeExpressions(
                                includeSpecification.NavigationPath,
                                entityParameter,
                                trackingQuery,
                                ref includedIndex,
                                navigationIndex: 0));
                    }

                    // TODO: Hack until new Include fully implemented
                    includeResultOperators.Remove(includeSpecification.IncludeResultOperator);
                }

                var includeReplacingExpressionVisitor
                    = new IncludeReplacingExpressionVisitor(
                        includeGrouping.Key,
                        Expression.Call(
                            _includeMethodInfo.MakeGenericMethod(includeGrouping.Key.Type),
                            includeGrouping.Key,
                            Expression.NewArrayInit(
                                typeof(object),
                                propertyExpressions),
                            Expression.Lambda(
                                Expression.Block(typeof(void), blockExpressions),
                                entityParameter,
                                _includedParameter)));

                queryModel.SelectClause.TransformExpressions(includeReplacingExpressionVisitor.Visit);
            }
        }

        private static QueryModel BuildCollectionIncludeQueryModel(
            QueryModel parentQueryModel,
            INavigation navigation,
            QuerySourceReferenceExpression parentQuerySourceReferenceExpression)
        {
            AppendPrincipalKeyOrderingToQuery(parentQueryModel, navigation, parentQuerySourceReferenceExpression);

            var parentQuerySource = parentQuerySourceReferenceExpression.ReferencedQuerySource;

            var parentQuerySourceIndex
                = parentQueryModel.BodyClauses
                    .IndexOf(parentQuerySource as IBodyClause);

            var parentItemName
                = parentQuerySource.HasGeneratedItemName()
                    ? navigation.DeclaringEntityType.DisplayName()[0].ToString().ToLowerInvariant()
                    : parentQuerySource.ItemName;

            QueryModel collectionQueryModel;

            if (!parentQueryModel.ResultOperators.Any(
                r => r is SkipResultOperator || r is TakeResultOperator)
                && !parentQueryModel.BodyClauses.Any(bc => bc is AdditionalFromClause))
            {
                collectionQueryModel = parentQueryModel.Clone();

                var clonedParentQuerySource
                    = parentQuerySourceIndex > 0
                        ? (IQuerySource)collectionQueryModel.BodyClauses[parentQuerySourceIndex]
                        : collectionQueryModel.MainFromClause;

                var collectionItemClrType = navigation.GetTargetType().ClrType;

                var joinClause
                    = new JoinClause(
                        $"{parentItemName}.{navigation.Name}",
                        collectionItemClrType,
                        NullAsyncQueryProvider.Instance
                            .CreateEntityQueryableExpression(collectionItemClrType),
                        EntityQueryModelVisitor.CreatePropertyExpression(
                            new QuerySourceReferenceExpression(clonedParentQuerySource),
                            navigation.ForeignKey.Properties[0]),
                        Expression.Constant(null));

                var joinInnerQuerySourceReferenceExpression = new QuerySourceReferenceExpression(joinClause);

                joinClause.InnerKeySelector
                    = EntityQueryModelVisitor.CreatePropertyExpression(
                        joinInnerQuerySourceReferenceExpression,
                        navigation.ForeignKey.PrincipalKey.Properties[0]);

                collectionQueryModel.BodyClauses.Insert(0, joinClause);
                collectionQueryModel.SelectClause = new SelectClause(joinInnerQuerySourceReferenceExpression);
                collectionQueryModel.ResultTypeOverride
                    = typeof(IQueryable<>).MakeGenericType(joinInnerQuerySourceReferenceExpression.Type);
            }
            else
            {
                var collectionMainFromClause
                    = new MainFromClause(
                        $"{parentItemName}.{navigation.Name}",
                        navigation.GetTargetType().ClrType,
                        NullAsyncQueryProvider.Instance
                            .CreateEntityQueryableExpression(navigation.GetTargetType().ClrType));

                var collectionQuerySourceReferenceExpression
                    = new QuerySourceReferenceExpression(collectionMainFromClause);

                collectionQueryModel
                    = new QueryModel(
                        collectionMainFromClause,
                        new SelectClause(collectionQuerySourceReferenceExpression));

                if (!parentQueryModel.IsIdentityQuery()
                    || parentQueryModel.ResultOperators.Any())
                {
                    var clonedParentQueryModel = parentQueryModel.Clone();

                    var clonedParentQuerySource
                        = parentQuerySourceIndex > 0
                            ? (IQuerySource)clonedParentQueryModel.BodyClauses[parentQuerySourceIndex]
                            : clonedParentQueryModel.MainFromClause;

                    var clonedParentQuerySourceReferenceExpression
                        = new QuerySourceReferenceExpression(clonedParentQuerySource);

                    clonedParentQueryModel.SelectClause 
                        = new SelectClause(clonedParentQuerySourceReferenceExpression);

                    clonedParentQueryModel.ResultTypeOverride
                        = typeof(IQueryable<>).MakeGenericType(clonedParentQuerySourceReferenceExpression.Type);

                    var subQueryExpression = new SubQueryExpression(clonedParentQueryModel);

                    var joinClause
                        = new JoinClause(
                            clonedParentQuerySource.ItemName,
                            clonedParentQuerySource.ItemType,
                            subQueryExpression,
                            EntityQueryModelVisitor.CreatePropertyExpression(
                                collectionQuerySourceReferenceExpression,
                                navigation.ForeignKey.Properties[0]),
                            Expression.Constant(null));

                    var joinQuerySourceReferenceExpression = new QuerySourceReferenceExpression(joinClause);

                    joinClause.InnerKeySelector
                        = EntityQueryModelVisitor.CreatePropertyExpression(
                            joinQuerySourceReferenceExpression,
                            navigation.ForeignKey.PrincipalKey.Properties[0]);

                    collectionQueryModel.BodyClauses.Add(joinClause);

                    LiftOrderBy(
                        querySource: clonedParentQuerySource, 
                        targetExpression: joinQuerySourceReferenceExpression, 
                        fromQueryModel: clonedParentQueryModel, 
                        toQueryModel: collectionQueryModel);
                }
            }

            return collectionQueryModel;
        }

        private static void LiftOrderBy(
            IQuerySource querySource, Expression targetExpression, QueryModel fromQueryModel, QueryModel toQueryModel)
        {
            var canRemove 
                = !fromQueryModel.ResultOperators
                    .Any(r => r is SkipResultOperator || r is TakeResultOperator);

            var querySourceMapping = new QuerySourceMapping();

            querySourceMapping.AddMapping(querySource, targetExpression);

            foreach (var orderByClause in fromQueryModel.BodyClauses.OfType<OrderByClause>().ToArray())
            {
                var outerOrderByClause = new OrderByClause();

                foreach (var ordering in orderByClause.Orderings)
                {
                    outerOrderByClause.Orderings
                        .Add(
                            new Ordering(
                                ReferenceReplacingExpressionVisitor
                                    .ReplaceClauseReferences(
                                        ordering.Expression,
                                        querySourceMapping,
                                        throwOnUnmappedReferences: false),
                                ordering.OrderingDirection));
                }

                toQueryModel.BodyClauses.Add(outerOrderByClause);

                if (canRemove)
                {
                    fromQueryModel.BodyClauses.Remove(orderByClause);
                }
            }
        }

        private static void AppendPrincipalKeyOrderingToQuery(
            QueryModel queryModel, INavigation navigation, Expression expression)
        {
            var orderByClause = queryModel.BodyClauses.OfType<OrderByClause>().LastOrDefault();

            if (orderByClause == null)
            {
                orderByClause = new OrderByClause();
                queryModel.BodyClauses.Add(orderByClause);
            }

            foreach (var property in navigation.ForeignKey.PrincipalKey.Properties)
            {
                if (!ContainsOrdering(orderByClause, expression, property))
                {
                    orderByClause.Orderings.Add(
                        new Ordering(
                            EntityQueryModelVisitor.CreatePropertyExpression(expression, property),
                            OrderingDirection.Asc));
                }
            }
        }

        private static bool ContainsOrdering(OrderByClause orderByClause, Expression expression, IProperty property)
        {
            foreach (var ordering in orderByClause.Orderings)
            {
                switch (ordering.Expression)
                {
                    case MemberExpression memberExpression
                    when memberExpression.Expression.Equals(expression)
                         && memberExpression.Member.Equals(property.PropertyInfo):
                        return true;
                    case MethodCallExpression methodCallExpression
                    when EntityQueryModelVisitor.IsPropertyMethod(methodCallExpression.Method)
                         && methodCallExpression.Arguments[0].Equals(expression)
                         && ((ConstantExpression)methodCallExpression.Arguments[1]).Value.Equals(property.Name):
                        return true;
                }
            }

            return false;
        }

        private Expression BuildCollectionIncludeExpressions(
            INavigation navigation,
            Expression targetEntityExpression,
            bool trackingQuery,
            ref int includedIndex)
        {
            var collectionFuncArrayAccessExpression
                = Expression.ArrayAccess(_includedParameter, Expression.Constant(includedIndex++));

            var relatedCollectionFuncExpression
                = Expression.Convert(
                    collectionFuncArrayAccessExpression,
                    typeof(Func<IEnumerable<object>>));

            var inverseNavigation = navigation.FindInverse();

            return Expression.Call(
                Expression.Property(
                    EntityQueryModelVisitor.QueryContextParameter,
                    propertyName: "QueryBuffer"),
                _queryBufferIncludeCollectionMethodInfo,
                Expression.Constant(_collectionIncludeId++),
                Expression.Constant(navigation),
                Expression.Constant(inverseNavigation),
                Expression.Constant(navigation.GetTargetType()),
                Expression.Constant(navigation.GetCollectionAccessor()),
                Expression.Constant(inverseNavigation?.GetSetter()),
                Expression.Constant(trackingQuery),
                targetEntityExpression,
                relatedCollectionFuncExpression);
        }

        private IEnumerable<IncludeSpecification> CreateIncludeSpecifications(
            QueryModel queryModel,
            IEnumerable<IncludeResultOperator> includeResultOperators)
        {
            var querySourceTracingExpressionVisitor
                = _querySourceTracingExpressionVisitorFactory.Create();

            return includeResultOperators
                .Select(
                    includeResultOperator =>
                        {
                            var entityType
                                = _queryCompilationContext.Model
                                    .FindEntityType(includeResultOperator.PathFromQuerySource.Type);

                            var parts = includeResultOperator.NavigationPropertyPaths.ToArray();
                            var navigationPath = new INavigation[parts.Length];

                            for (var i = 0; i < parts.Length; i++)
                            {
                                navigationPath[i] = entityType.FindNavigation(parts[i]);

                                if (navigationPath[i] == null)
                                {
                                    throw new InvalidOperationException(
                                        CoreStrings.IncludeBadNavigation(parts[i], entityType.DisplayName()));
                                }

                                entityType = navigationPath[i].GetTargetType();
                            }

                            var querySourceReferenceExpression
                                = querySourceTracingExpressionVisitor
                                    .FindResultQuerySourceReferenceExpression(
                                        queryModel.SelectClause.Selector,
                                        includeResultOperator.QuerySource);

                            if (querySourceReferenceExpression == null)
                            {
                                _queryCompilationContext.Logger
                                    .LogWarning(
                                        CoreEventId.IncludeIgnoredWarning,
                                        () => CoreStrings.LogIgnoredInclude(
                                            $"{includeResultOperator.QuerySource.ItemName}.{navigationPath.Select(n => n.Name).Join(".")}"));
                            }

                            return new IncludeSpecification(
                                includeResultOperator,
                                querySourceReferenceExpression,
                                navigationPath);
                        })
                .Where(
                    a =>
                        {
                            if (a.QuerySourceReferenceExpression == null)
                            {
                                return false;
                            }

                            var sequenceType = a.QuerySourceReferenceExpression.Type.TryGetSequenceType();

                            if (sequenceType != null
                                && _queryCompilationContext.Model.FindEntityType(sequenceType) != null)
                            {
                                return false;
                            }

                            return !a.NavigationPath.Any(n => n.IsCollection())
                                   || a.NavigationPath.Length == 1;
                        })
                .ToArray();
        }

        private static Expression BuildIncludeExpressions(
            IReadOnlyList<INavigation> navigationPath,
            Expression targetEntityExpression,
            bool trackingQuery,
            ref int includedIndex,
            int navigationIndex)
        {
            var navigation = navigationPath[navigationIndex];

            var relatedArrayAccessExpression
                = Expression.ArrayAccess(_includedParameter, Expression.Constant(includedIndex++));

            var relatedEntityExpression
                = Expression.Convert(relatedArrayAccessExpression, navigation.ClrType);

            var stateManagerProperty
                = Expression.Property(
                    Expression.Property(
                        EntityQueryModelVisitor.QueryContextParameter,
                        propertyName: "StateManager"),
                    propertyName: "Value");

            var blockExpressions = new List<Expression>();

            if (trackingQuery)
            {
                blockExpressions.Add(
                    Expression.Call(
                        Expression.Property(
                            EntityQueryModelVisitor.QueryContextParameter,
                            propertyName: "QueryBuffer"),
                        _queryBufferStartTrackingMethodInfo,
                        relatedArrayAccessExpression,
                        Expression.Constant(navigation.GetTargetType())));

                blockExpressions.Add(
                    Expression.Call(
                        _setRelationshipSnapshotValueMethodInfo,
                        stateManagerProperty,
                        Expression.Constant(navigation),
                        targetEntityExpression,
                        relatedArrayAccessExpression));
            }
            else
            {
                blockExpressions.Add(
                    Expression.Assign(
                        Expression.MakeMemberAccess(
                            targetEntityExpression,
                            navigation.GetMemberInfo(false, true)),
                        relatedEntityExpression));
            }

            var inverseNavigation = navigation.FindInverse();

            if (inverseNavigation != null)
            {
                var collection = inverseNavigation.IsCollection();

                if (trackingQuery)
                {
                    blockExpressions.Add(
                        Expression.Call(
                            collection
                                ? _addToCollectionSnapshotMethodInfo
                                : _setRelationshipSnapshotValueMethodInfo,
                            stateManagerProperty,
                            Expression.Constant(inverseNavigation),
                            relatedArrayAccessExpression,
                            targetEntityExpression));
                }
                else
                {
                    blockExpressions.Add(
                        collection
                            ? (Expression)Expression.Call(
                                Expression.Constant(inverseNavigation.GetCollectionAccessor()),
                                _collectionAccessorAddMethodInfo,
                                relatedArrayAccessExpression,
                                targetEntityExpression)
                            : Expression.Assign(
                                Expression.MakeMemberAccess(
                                    relatedEntityExpression,
                                    inverseNavigation
                                        .GetMemberInfo(forConstruction: false, forSet: true)),
                                targetEntityExpression));
                }
            }

            if (navigationIndex < navigationPath.Count - 1)
            {
                blockExpressions.Add(
                    BuildIncludeExpressions(
                        navigationPath,
                        relatedEntityExpression,
                        trackingQuery,
                        ref includedIndex,
                        navigationIndex + 1));
            }

            return
                Expression.IfThen(
                    Expression.Not(
                        Expression.Call(
                            _referenceEqualsMethodInfo,
                            relatedArrayAccessExpression,
                            Expression.Constant(null, typeof(object)))),
                    Expression.Block(typeof(void), blockExpressions));
        }

        private class IncludeReplacingExpressionVisitor : RelinqExpressionVisitor
        {
            private QuerySourceReferenceExpression _querySourceReferenceExpression;
            private readonly Expression _includeExpression;

            public IncludeReplacingExpressionVisitor(
                QuerySourceReferenceExpression querySourceReferenceExpression, Expression includeExpression)
            {
                _querySourceReferenceExpression = querySourceReferenceExpression;
                _includeExpression = includeExpression;
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
        public static bool IsIncludeMethod(MethodCallExpression methodCallExpression)
            => methodCallExpression.Method.IsGenericMethod
               && Equals(methodCallExpression.Method.GetGenericMethodDefinition(), _includeMethodInfo);

        private static readonly MethodInfo _includeMethodInfo
            = typeof(IncludeCompiler).GetTypeInfo()
                .GetDeclaredMethod(nameof(_Include));

        // ReSharper disable once InconsistentNaming
        private static TEntity _Include<TEntity>(
            TEntity entity,
            object[] included,
            Action<TEntity, object[]> fixup)
        {
            fixup(entity, included);

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
