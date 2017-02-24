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
                CompileIncludes(
                    queryModel,
                    includeResultOperators,
                    includeGrouping.ToArray(),
                    trackResults: trackingQuery);
            }
        }

        private void CompileIncludes(
            QueryModel queryModel,
            ICollection<IncludeResultOperator> includeResultOperators,
            IReadOnlyCollection<IncludeSpecification> includeSpecifications,
            bool trackResults)
        {
            var querySourceReferenceExpression = includeSpecifications.First().QuerySourceReferenceExpression;

            if (querySourceReferenceExpression.ReferencedQuerySource is GroupJoinClause groupJoinClause)
            {
                CompileGroupJoinInclude(includeResultOperators, includeSpecifications, groupJoinClause);
            }
            else
            {
                var entityType = includeSpecifications.First().NavigationPath[0].DeclaringEntityType;
                var entityParameter = Expression.Parameter(entityType.ClrType, name: "entity");

                var propertyExpressions = new List<Expression>();
                var blockExpressions = new List<Expression>();

                if (trackResults)
                {
                    blockExpressions.Add(
                        Expression.Call(
                            Expression.Property(
                                EntityQueryModelVisitor.QueryContextParameter,
                                propertyName: "QueryBuffer"),
                            _queryBufferStartTrackingMethodInfo,
                            entityParameter,
                            Expression.Constant(entityType)));
                }

                var includedIndex = 0;

                foreach (var includeSpecification in includeSpecifications)
                {
                    _queryCompilationContext.Logger
                        .LogDebug(
                            CoreEventId.IncludingNavigation,
                            () => CoreStrings.LogIncludingNavigation(includeSpecification.IncludeResultOperator));

                    var navigation = includeSpecification.NavigationPath[0];

                    if (navigation.IsCollection())
                    {
                        AddOrderingsToOuterQuery(queryModel, navigation, includeSpecification.QuerySourceReferenceExpression);

                        propertyExpressions.Add(
                            Expression.Lambda<Func<IEnumerable<object>>>(
                                new SubQueryExpression(
                                    BuildCollectionIncludeQueryModel(queryModel, navigation, includeSpecification.QuerySourceReferenceExpression))));

                        blockExpressions.Add(
                            BuildCollectionIncludeExpressions(
                                navigation,
                                entityParameter,
                                trackResults,
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
                                trackResults,
                                ref includedIndex,
                                navigationIndex: 0));
                    }

                    // TODO: Hack until new Include fully implemented
                    includeResultOperators.Remove(includeSpecification.IncludeResultOperator);
                }

                var includeReplacingExpressionVisitor
                    = new IncludeReplacingExpressionVisitor(
                        querySourceReferenceExpression,
                        Expression.Call(
                            _includeMethodInfo.MakeGenericMethod(querySourceReferenceExpression.Type),
                            querySourceReferenceExpression,
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

        private static void AddOrderingsToOuterQuery(QueryModel queryModel, INavigation navigation, Expression expression)
        {
            var orderByClause = queryModel.BodyClauses.OfType<OrderByClause>().LastOrDefault();

            if (orderByClause == null)
            {
                orderByClause = new OrderByClause();
                queryModel.BodyClauses.Add(orderByClause);
            }

            foreach (var property in navigation.ForeignKey.PrincipalKey.Properties)
            {
                orderByClause.Orderings.Add(
                    new Ordering(
                        EntityQueryModelVisitor.CreatePropertyExpression(expression, property),
                        OrderingDirection.Asc));
            }
        }

        private static QueryModel BuildCollectionIncludeQueryModel(
            QueryModel parentQueryModel, INavigation navigation, QuerySourceReferenceExpression outerExpression)
        {
            var mainFromClause
                = new MainFromClause(
                    navigation.Name,
                    navigation.GetTargetType().ClrType,
                    NullAsyncQueryProvider.Instance.CreateEntityQueryableExpression(
                        navigation.GetTargetType().ClrType));

            var innerQuerySourceReferenceExpression = new QuerySourceReferenceExpression(mainFromClause);

            var queryModel
                = new QueryModel(
                    mainFromClause,
                    new SelectClause(innerQuerySourceReferenceExpression));

            if (!parentQueryModel.IsIdentityQuery())
            {
                var outerQuerySourceIndex
                    = parentQueryModel.BodyClauses.IndexOf((IBodyClause)outerExpression.ReferencedQuerySource);

                var subQueryModel = parentQueryModel.Clone();

                var outerQuerySource
                    = outerQuerySourceIndex > 0
                        ? (IClause)subQueryModel.BodyClauses[outerQuerySourceIndex]
                        : subQueryModel.MainFromClause;

                Expression joinPredicate 
                    =Expression.Equal(
                    EntityQueryModelVisitor.CreatePropertyExpression(
                        outerExpression, navigation.ForeignKey.PrincipalKey.Properties[0]),
                    EntityQueryModelVisitor.CreatePropertyExpression(
                        innerQuerySourceReferenceExpression, navigation.ForeignKey.Properties[0]));

                var whereClause = new WhereClause(joinPredicate);

                subQueryModel.BodyClauses.Insert(0, whereClause);
                subQueryModel.SelectClause.Selector = Expression.Constant(1);
                subQueryModel.ResultOperators.Add(new AnyResultOperator());
                subQueryModel.ResultTypeOverride = typeof(bool);
                
                queryModel.BodyClauses.Add(new WhereClause(new SubQueryExpression(subQueryModel)));
            }
            
            var orderByClause = new OrderByClause();

            foreach (var property in navigation.ForeignKey.Properties)
            {
                orderByClause.Orderings.Add(
                    new Ordering(
                        EntityQueryModelVisitor.CreatePropertyExpression(
                            innerQuerySourceReferenceExpression, property),
                        OrderingDirection.Asc));
            }

            queryModel.BodyClauses.Add(orderByClause);

            return queryModel;
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

        private void CompileGroupJoinInclude(
            ICollection<IncludeResultOperator> includeResultOperators,
            IReadOnlyCollection<IncludeSpecification> includeSpecifications,
            GroupJoinClause groupJoinClause)
        {
            var joinClause = groupJoinClause.JoinClause;

            var subQueryModel = (joinClause.InnerSequence as SubQueryExpression)?.QueryModel;

            QuerySourceReferenceExpression innerQuerySourceReferenceExpression;

            if (subQueryModel == null)
            {
                var mainFromClause
                    = new MainFromClause(joinClause.ItemName, joinClause.ItemType, joinClause.InnerSequence);

                innerQuerySourceReferenceExpression = new QuerySourceReferenceExpression(mainFromClause);

                subQueryModel = new QueryModel(
                    mainFromClause,
                    new SelectClause(innerQuerySourceReferenceExpression));

                groupJoinClause.JoinClause.InnerSequence = new SubQueryExpression(subQueryModel);

                foreach (var queryAnnotation in _queryCompilationContext.QueryAnnotations)
                {
                    queryAnnotation.QuerySource = mainFromClause;
                }
            }
            else
            {
                var querySourceTracingExpressionVisitor
                    = _querySourceTracingExpressionVisitorFactory.Create();

                innerQuerySourceReferenceExpression
                    = querySourceTracingExpressionVisitor
                        .FindResultQuerySourceReferenceExpression(
                            subQueryModel.SelectClause.Selector,
                            includeSpecifications.First().IncludeResultOperator.QuerySource);
            }

            CompileIncludes(
                subQueryModel,
                includeResultOperators,
                includeSpecifications: includeSpecifications
                    .Select(
                        @is =>
                            new IncludeSpecification(
                                @is.IncludeResultOperator,
                                innerQuerySourceReferenceExpression,
                                @is.NavigationPath))
                    .ToArray(), trackResults: false);
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
