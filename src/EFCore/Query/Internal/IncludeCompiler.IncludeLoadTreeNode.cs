// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ExpressionVisitors;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    public partial class IncludeCompiler
    {
        private sealed class IncludeLoadTreeNode : IncludeLoadTreeNodeBase
        {
            private static readonly ExpressionEqualityComparer _expressionEqualityComparer 
                = new ExpressionEqualityComparer();

            public IncludeLoadTreeNode(INavigation navigation) => Navigation = navigation;

            public INavigation Navigation { get; }

            public Expression BuildIncludeExpressions(
                QueryCompilationContext queryCompilationContext,
                NavigationRewritingExpressionVisitor navigationRewritingExpressionVisitor,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                QuerySourceReferenceExpression parentQuerySourceReferenceExpression,
                ICollection<Ordering> parentOrderings,
                Expression entityParameterExpression,
                ICollection<Expression> propertyExpressions,
                ref int includedIndex,
                ref int collectionIncludeId) 
                => Navigation.IsCollection()
                ? BuildCollectionIncludeExpressions(
                    queryCompilationContext,
                    navigationRewritingExpressionVisitor,
                    queryModel,
                    trackingQuery,
                    asyncQuery,
                    parentQuerySourceReferenceExpression,
                    parentOrderings,
                    entityParameterExpression,
                    ref collectionIncludeId)
                : BuildReferenceIncludeExpressions(
                    queryCompilationContext,
                    navigationRewritingExpressionVisitor,
                    queryModel,
                    trackingQuery,
                    asyncQuery,
                    parentQuerySourceReferenceExpression,
                    parentOrderings,
                    entityParameterExpression,
                    propertyExpressions,
                    ref includedIndex,
                    ref collectionIncludeId);

            private Expression BuildCollectionIncludeExpressions(
                QueryCompilationContext queryCompilationContext,
                NavigationRewritingExpressionVisitor navigationRewritingExpressionVisitor,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                QuerySourceReferenceExpression parentQuerySourceReferenceExpression,
                ICollection<Ordering> parentOrderings,
                Expression targetEntityExpression,
                ref int collectionIncludeId)
            {
                var collectionIncludeQueryModel
                    = BuildCollectionIncludeQueryModel(
                        queryCompilationContext,
                        queryModel,
                        Navigation,
                        parentQuerySourceReferenceExpression,
                        parentOrderings);

                var collectionQuerySourceReferenceExpression
                    = (QuerySourceReferenceExpression)collectionIncludeQueryModel.SelectClause.Selector;

                queryCompilationContext
                    .AddQuerySourceRequiringMaterialization(collectionQuerySourceReferenceExpression.ReferencedQuerySource);

                if (Children.Count > 0)
                {
                    Compile(
                        queryCompilationContext,
                        navigationRewritingExpressionVisitor,
                        collectionIncludeQueryModel,
                        trackingQuery,
                        asyncQuery,
                        collectionQuerySourceReferenceExpression,
                        parentOrderings,
                        ref collectionIncludeId);
                }

                Expression collectionLambdaExpression
                    = Expression.Lambda<Func<IEnumerable<object>>>(
                        new SubQueryExpression(collectionIncludeQueryModel));

                var includeCollectionMethodInfo = _queryBufferIncludeCollectionMethodInfo;

                Expression cancellationTokenExpression = null;

                if (asyncQuery)
                {
                    collectionLambdaExpression
                        = Expression.Convert(
                            collectionLambdaExpression,
                            typeof(Func<IAsyncEnumerable<object>>));

                    includeCollectionMethodInfo = _queryBufferIncludeCollectionAsyncMethodInfo;
                    cancellationTokenExpression = _cancellationTokenParameter;
                }

                var inverseNavigation = Navigation.FindInverse();

                var arguments = new List<Expression>
                {
                    Expression.Constant(collectionIncludeId++),
                    Expression.Constant(Navigation),
                    Expression.Constant(inverseNavigation, typeof(INavigation)),
                    Expression.Constant(Navigation.GetTargetType()),
                    Expression.Constant(Navigation.GetCollectionAccessor()),
                    Expression.Constant(inverseNavigation?.GetSetter(), typeof(IClrPropertySetter)),
                    Expression.Constant(trackingQuery),
                    targetEntityExpression,
                    collectionLambdaExpression
                };

                if (cancellationTokenExpression != null)
                {
                    arguments.Add(cancellationTokenExpression);
                }

                return Expression.Call(
                    Expression.Property(
                        EntityQueryModelVisitor.QueryContextParameter,
                        nameof(QueryContext.QueryBuffer)),
                    includeCollectionMethodInfo,
                    arguments);
            }

            private static QueryModel BuildCollectionIncludeQueryModel(
                QueryCompilationContext queryCompilationContext,
                QueryModel parentQueryModel,
                INavigation navigation,
                QuerySourceReferenceExpression parentQuerySourceReferenceExpression,
                ICollection<Ordering> parentOrderings)
            {
                BuildParentOrderings(
                    parentQueryModel,
                    navigation,
                    parentQuerySourceReferenceExpression,
                    parentOrderings);

                var parentQuerySource = parentQuerySourceReferenceExpression.ReferencedQuerySource;

                var parentItemName
                    = parentQuerySource.HasGeneratedItemName()
                        ? navigation.DeclaringEntityType.DisplayName()[0].ToString().ToLowerInvariant()
                        : parentQuerySource.ItemName;

                var collectionMainFromClause
                    = new MainFromClause(
                        $"{parentItemName}.{navigation.Name}",
                        navigation.GetTargetType().ClrType,
                        NullAsyncQueryProvider.Instance
                            .CreateEntityQueryableExpression(navigation.GetTargetType().ClrType));

                var collectionQuerySourceReferenceExpression
                    = new QuerySourceReferenceExpression(collectionMainFromClause);

                var collectionQueryModel
                    = new QueryModel(
                        collectionMainFromClause,
                        new SelectClause(collectionQuerySourceReferenceExpression));

                var querySourceMapping = new QuerySourceMapping();
                var clonedParentQueryModel = parentQueryModel.Clone(querySourceMapping);

                queryCompilationContext.CloneAnnotations(querySourceMapping, clonedParentQueryModel);

                var clonedParentQuerySourceReferenceExpression
                    = (QuerySourceReferenceExpression)querySourceMapping.GetExpression(parentQuerySource);

                var clonedParentQuerySource
                    = clonedParentQuerySourceReferenceExpression.ReferencedQuerySource;

                AdjustPredicate(
                    clonedParentQueryModel,
                    clonedParentQuerySource,
                    clonedParentQuerySourceReferenceExpression);

                clonedParentQueryModel.SelectClause
                    = new SelectClause(Expression.Default(typeof(CompositeKey)));

                var subQueryProjection = new List<Expression>();

                var lastResultOperator = ProcessResultOperators(clonedParentQueryModel);

                clonedParentQueryModel.ResultTypeOverride
                    = typeof(IQueryable<>).MakeGenericType(clonedParentQueryModel.SelectClause.Selector.Type);

                var joinQuerySourceReferenceExpression
                    = CreateJoinToParentQuery(
                        clonedParentQueryModel,
                        clonedParentQuerySourceReferenceExpression,
                        collectionQuerySourceReferenceExpression,
                        navigation.ForeignKey,
                        collectionQueryModel,
                        subQueryProjection);

                ApplyParentOrderings(
                    parentOrderings,
                    clonedParentQueryModel,
                    querySourceMapping,
                    lastResultOperator);

                LiftOrderBy(
                    clonedParentQuerySource,
                    joinQuerySourceReferenceExpression,
                    clonedParentQueryModel,
                    collectionQueryModel,
                    subQueryProjection);

                clonedParentQueryModel.SelectClause.Selector
                    = Expression.New(
                        CompositeKey.CompositeKeyCtor,
                        Expression.NewArrayInit(
                            typeof(object),
                            subQueryProjection));

                return collectionQueryModel;
            }

            private static void BuildParentOrderings(
                QueryModel queryModel,
                INavigation navigation,
                Expression querySourceReferenceExpression,
                ICollection<Ordering> parentOrderings)
            {
                var orderings = parentOrderings;

                var orderByClause
                    = queryModel.BodyClauses.OfType<OrderByClause>().LastOrDefault();

                if (orderByClause != null)
                {
                    orderings = orderings.Concat(orderByClause.Orderings).ToArray();
                }

                foreach (var property in navigation.ForeignKey.PrincipalKey.Properties)
                {
                    var propertyExpression
                        = EntityQueryModelVisitor
                            .CreatePropertyExpression(querySourceReferenceExpression, property);

                    if (!orderings.Any(o =>
                        _expressionEqualityComparer.Equals(o.Expression, propertyExpression)
                        || o.Expression is MemberExpression memberExpression
                        && memberExpression.Expression == querySourceReferenceExpression
                        && memberExpression.Member.Equals(property.PropertyInfo)))
                    {
                        parentOrderings.Add(new Ordering(propertyExpression, OrderingDirection.Asc));
                    }
                }
            }

            private static void AdjustPredicate(
                QueryModel queryModel,
                IQuerySource parentQuerySource,
                Expression targetParentExpression)
            {
                var querySourcePriorityAnalyzer
                    = new QuerySourcePriorityAnalyzer(queryModel.SelectClause.Selector);

                Expression predicate = null;

                if (querySourcePriorityAnalyzer.AreLowerPriorityQuerySources(parentQuerySource))
                {
                    predicate
                        = Expression.NotEqual(
                            targetParentExpression,
                            Expression.Constant(null, targetParentExpression.Type));
                }

                predicate
                    = querySourcePriorityAnalyzer.GetHigherPriorityQuerySources(parentQuerySource)
                        .Select(qs => new QuerySourceReferenceExpression(qs))
                        .Select(qsre => Expression.Equal(qsre, Expression.Constant(null, qsre.Type)))
                        .Aggregate(
                            predicate,
                            (current, nullCheck)
                                => current == null
                                    ? nullCheck
                                    : Expression.AndAlso(current, nullCheck));

                if (predicate != null)
                {
                    var whereClause = queryModel.BodyClauses.OfType<WhereClause>().LastOrDefault();

                    if (whereClause == null)
                    {
                        queryModel.BodyClauses.Add(new WhereClause(predicate));
                    }
                    else
                    {
                        whereClause.Predicate = Expression.AndAlso(whereClause.Predicate, predicate);
                    }
                }
            }

            private sealed class QuerySourcePriorityAnalyzer : RelinqExpressionVisitor
            {
                private readonly List<IQuerySource> _querySources = new List<IQuerySource>();

                public QuerySourcePriorityAnalyzer(Expression expression)
                {
                    Visit(expression);
                }

                public bool AreLowerPriorityQuerySources(IQuerySource querySource)
                {
                    var index = _querySources.IndexOf(querySource);

                    return index != -1 && index < _querySources.Count - 1;
                }

                public IEnumerable<IQuerySource> GetHigherPriorityQuerySources(IQuerySource querySource)
                {
                    var index = _querySources.IndexOf(querySource);

                    if (index != -1)
                    {
                        for (var i = 0; i < index; i++)
                        {
                            yield return _querySources[i];
                        }
                    }
                }

                protected override Expression VisitBinary(BinaryExpression node)
                {
                    IQuerySource querySource;

                    if (node.NodeType == ExpressionType.Coalesce
                        && (querySource = ExtractQuerySource(node.Left)) != null)
                    {
                        _querySources.Add(querySource);

                        if ((querySource = ExtractQuerySource(node.Right)) != null)
                        {
                            _querySources.Add(querySource);
                        }
                        else
                        {
                            Visit(node.Right);

                            return node;
                        }
                    }

                    return base.VisitBinary(node);
                }

                private static IQuerySource ExtractQuerySource(Expression expression)
                {
                    switch (expression)
                    {
                        case QuerySourceReferenceExpression querySourceReferenceExpression:
                            return querySourceReferenceExpression.ReferencedQuerySource;
                        case MethodCallExpression methodCallExpression
                        when IsIncludeMethod(methodCallExpression):
                            return ((QuerySourceReferenceExpression)methodCallExpression.Arguments[1]).ReferencedQuerySource;
                    }

                    return null;
                }
            }

            private static bool ProcessResultOperators(QueryModel queryModel)
            {
                var choiceResultOperator
                    = queryModel.ResultOperators.LastOrDefault() as ChoiceResultOperatorBase;

                var lastResultOperator = false;

                if (choiceResultOperator != null)
                {
                    queryModel.ResultOperators.Remove(choiceResultOperator);
                    queryModel.ResultOperators.Add(new TakeResultOperator(Expression.Constant(1)));

                    lastResultOperator = choiceResultOperator is LastResultOperator;
                }

                foreach (var groupResultOperator
                    in queryModel.ResultOperators.OfType<GroupResultOperator>()
                        .ToArray())
                {
                    queryModel.ResultOperators.Remove(groupResultOperator);

                    var orderByClause1 = queryModel.BodyClauses.OfType<OrderByClause>().LastOrDefault();

                    if (orderByClause1 == null)
                    {
                        queryModel.BodyClauses.Add(orderByClause1 = new OrderByClause());
                    }

                    orderByClause1.Orderings.Add(new Ordering(groupResultOperator.KeySelector, OrderingDirection.Asc));
                }

                if (queryModel.BodyClauses
                        .Count(
                            bc => bc is AdditionalFromClause
                                  || bc is JoinClause
                                  || bc is GroupJoinClause) > 0)
                {
                    queryModel.ResultOperators.Add(new DistinctResultOperator());
                }

                return lastResultOperator;
            }

            private static QuerySourceReferenceExpression CreateJoinToParentQuery(
                QueryModel parentQueryModel, 
                QuerySourceReferenceExpression parentQuerySourceReferenceExpression, 
                Expression outerTargetExpression, 
                IForeignKey foreignKey, 
                QueryModel targetQueryModel, 
                ICollection<Expression> subQueryProjection)
            {
                var subQueryExpression = new SubQueryExpression(parentQueryModel);
                var parentQuerySource = parentQuerySourceReferenceExpression.ReferencedQuerySource;

                var joinClause
                    = new JoinClause(
                        "_" + parentQuerySource.ItemName,
                        typeof(CompositeKey),
                        subQueryExpression,
                        CreateKeyAccessExpression(
                            outerTargetExpression,
                            foreignKey.Properties),
                        Expression.Constant(null));
                
                var joinQuerySourceReferenceExpression = new QuerySourceReferenceExpression(joinClause);
                var innerKeyExpressions = new List<Expression>();
                
                foreach (var principalKeyProperty in foreignKey.PrincipalKey.Properties)
                {
                    innerKeyExpressions.Add(
                        Expression.Convert(
                            Expression.Call(
                                joinQuerySourceReferenceExpression,
                                CompositeKey.GetValueMethodInfo,
                                Expression.Constant(subQueryProjection.Count)),
                            principalKeyProperty.ClrType.MakeNullable()));

                    subQueryProjection.Add(
                        Expression.Convert(
                            EntityQueryModelVisitor
                                .CreatePropertyExpression(
                                    parentQuerySourceReferenceExpression, 
                                    principalKeyProperty), 
                        typeof(object)));
                }

                joinClause.InnerKeySelector 
                    = innerKeyExpressions.Count == 1
                        ? innerKeyExpressions[0]
                        : Expression.New(
                            CompositeKey.CompositeKeyCtor,
                            Expression.NewArrayInit(
                                typeof(object), 
                                innerKeyExpressions.Select(e => Expression.Convert(e, typeof(object)))));

                targetQueryModel.BodyClauses.Add(joinClause);

                return joinQuerySourceReferenceExpression;
            }

            // TODO: Unify this with other versions
            private static Expression CreateKeyAccessExpression(
                Expression target, IReadOnlyList<IProperty> properties)
                => properties.Count == 1
                    ? EntityQueryModelVisitor
                        .CreatePropertyExpression(target, properties[0])
                    : Expression.New(
                        CompositeKey.CompositeKeyCtor,
                        Expression.NewArrayInit(
                            typeof(object),
                            properties
                                .Select(
                                    p =>
                                        Expression.Convert(
                                            EntityQueryModelVisitor.CreatePropertyExpression(target, p),
                                            typeof(object)))
                                .Cast<Expression>()
                                .ToArray()));

            private static void ApplyParentOrderings(
                IEnumerable<Ordering> parentOrderings,
                QueryModel queryModel,
                QuerySourceMapping querySourceMapping,
                bool reverseOrdering)
            {
                var orderByClause = queryModel.BodyClauses.OfType<OrderByClause>().LastOrDefault();

                if (orderByClause == null)
                {
                    queryModel.BodyClauses.Add(orderByClause = new OrderByClause());
                }
                
                foreach (var ordering in parentOrderings)
                {
                    var newExpression
                        = CloningExpressionVisitor
                            .AdjustExpressionAfterCloning(ordering.Expression, querySourceMapping);

                    orderByClause.Orderings
                        .Add(new Ordering(newExpression, ordering.OrderingDirection));
                }

                if (reverseOrdering)
                {
                    foreach (var ordering in orderByClause.Orderings)
                    {
                        ordering.OrderingDirection
                            = ordering.OrderingDirection == OrderingDirection.Asc
                                ? OrderingDirection.Desc
                                : OrderingDirection.Asc;
                    }
                }
            }

            private static void LiftOrderBy(
                IQuerySource querySource,
                Expression targetExpression,
                QueryModel fromQueryModel,
                QueryModel toQueryModel,
                List<Expression> subQueryProjection)
            {
                var canRemove
                    = !fromQueryModel.ResultOperators
                        .Any(r => r is SkipResultOperator || r is TakeResultOperator);

                foreach (var orderByClause 
                    in fromQueryModel.BodyClauses.OfType<OrderByClause>().ToArray())
                {
                    var outerOrderByClause = new OrderByClause();

                    foreach (var ordering in orderByClause.Orderings)
                    {
                        Expression orderingExpressionAsPropertyExpression = null;

                        if (ordering.Expression is MemberExpression memberExpression
                            && memberExpression.Expression 
                                is QuerySourceReferenceExpression querySourceReferenceExpression
                            && querySourceReferenceExpression.ReferencedQuerySource == querySource)
                        {
                            orderingExpressionAsPropertyExpression
                                = EntityQueryModelVisitor
                                    .CreatePropertyExpression(
                                        querySourceReferenceExpression,
                                        ordering.Expression.Type,
                                        memberExpression.Member.Name);
                        }

                        var projectionExpression
                            = Expression.Convert(
                                orderingExpressionAsPropertyExpression 
                                ?? ordering.Expression, typeof(object));

                        var projectionIndex 
                            = subQueryProjection
                                .FindIndex(e => _expressionEqualityComparer.Equals(e, projectionExpression));

                        if (projectionIndex == -1)
                        {
                            projectionIndex = subQueryProjection.Count;
                            subQueryProjection.Add(projectionExpression);
                        }

                        var newExpression
                            = Expression.Call(
                                targetExpression,
                                CompositeKey.GetValueMethodInfo,
                                Expression.Constant(projectionIndex));

                        outerOrderByClause.Orderings
                            .Add(new Ordering(newExpression, ordering.OrderingDirection));
                    }

                    toQueryModel.BodyClauses.Add(outerOrderByClause);

                    if (canRemove)
                    {
                        fromQueryModel.BodyClauses.Remove(orderByClause);
                    }
                }
            }

            private Expression BuildReferenceIncludeExpressions(
                QueryCompilationContext queryCompilationContext,
                NavigationRewritingExpressionVisitor navigationRewritingExpressionVisitor,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                QuerySourceReferenceExpression parentQuerySourceReferenceExpression,
                ICollection<Ordering> parentOrderings,
                Expression targetEntityExpression,
                ICollection<Expression> propertyExpressions,
                ref int includedIndex,
                ref int collectionIncludeId)
            {
                parentQuerySourceReferenceExpression
                    = (QuerySourceReferenceExpression)navigationRewritingExpressionVisitor
                        .Visit(
                            EntityQueryModelVisitor.CreatePropertyExpression(
                                parentQuerySourceReferenceExpression, Navigation));

                navigationRewritingExpressionVisitor.Rewrite(queryModel, null);

                propertyExpressions.Add(parentQuerySourceReferenceExpression);

                var relatedArrayAccessExpression
                    = Expression.ArrayAccess(_includedParameter, Expression.Constant(includedIndex++));

                var relatedEntityExpression
                    = Expression.Convert(relatedArrayAccessExpression, Navigation.ClrType);

                var stateManagerProperty
                    = Expression.Property(
                        Expression.Property(
                            EntityQueryModelVisitor.QueryContextParameter,
                            nameof(QueryContext.StateManager)),
                        nameof(Lazy<object>.Value));

                var blockExpressions = new List<Expression>();

                if (trackingQuery)
                {
                    blockExpressions.Add(
                        Expression.Call(
                            Expression.Property(
                                EntityQueryModelVisitor.QueryContextParameter,
                                nameof(QueryContext.QueryBuffer)),
                            _queryBufferStartTrackingMethodInfo,
                            relatedArrayAccessExpression,
                            Expression.Constant(Navigation.GetTargetType())));

                    blockExpressions.Add(
                        Expression.Call(
                            _setRelationshipSnapshotValueMethodInfo,
                            stateManagerProperty,
                            Expression.Constant(Navigation),
                            targetEntityExpression,
                            relatedArrayAccessExpression));
                }
                else
                {
                    blockExpressions.Add(
                        Expression.Assign(
                            Expression.MakeMemberAccess(
                                targetEntityExpression,
                                Navigation.GetMemberInfo(false, true)),
                            relatedEntityExpression));
                }

                var inverseNavigation = Navigation.FindInverse();

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

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var includeLoadTreeNode in Children)
                {
                    blockExpressions.Add(
                        includeLoadTreeNode
                            .BuildIncludeExpressions(
                                queryCompilationContext,
                                navigationRewritingExpressionVisitor,
                                queryModel,
                                trackingQuery,
                                asyncQuery,
                                parentQuerySourceReferenceExpression,
                                parentOrderings,
                                relatedEntityExpression,
                                propertyExpressions,
                                ref includedIndex,
                                ref collectionIncludeId));
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
        }
    }
}
