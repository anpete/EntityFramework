// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
            public IncludeLoadTreeNode(INavigation navigation) => Navigation = navigation;

            public INavigation Navigation { get; }

            public Expression BuildIncludeExpressions(
                QueryCompilationContext queryCompilationContext,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                QuerySourceReferenceExpression targetExpression,
                ICollection<Ordering> parentOrderings,
                Expression entityParameter,
                ICollection<Expression> propertyExpressions,
                ref int includedIndex,
                ref int collectionIncludeId)
            {
                return
                    Navigation.IsCollection()
                        ? BuildCollectionIncludeExpressions(
                            queryCompilationContext,
                            queryModel,
                            asyncQuery,
                            targetExpression,
                            parentOrderings,
                            entityParameter,
                            trackingQuery,
                            ref collectionIncludeId)
                        : BuildReferenceIncludeExpressions(
                            entityParameter,
                            trackingQuery,
                            propertyExpressions,
                            targetExpression,
                            ref includedIndex);
            }

            private Expression BuildCollectionIncludeExpressions(
                QueryCompilationContext queryCompilationContext,
                QueryModel queryModel,
                bool asyncQuery,
                QuerySourceReferenceExpression targetExpression,
                ICollection<Ordering> parentOrderings,
                Expression targetEntityExpression,
                bool trackingQuery,
                ref int collectionIncludeId)
            {
                var collectionIncludeQueryModel
                    = BuildCollectionIncludeQueryModel(
                        queryCompilationContext,
                        queryModel,
                        Navigation,
                        targetExpression,
                        parentOrderings);

                queryCompilationContext.AddQuerySourceRequiringMaterialization(
                    ((QuerySourceReferenceExpression)collectionIncludeQueryModel.SelectClause.Selector)
                    .ReferencedQuerySource);

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
                CollectParentOrderings(
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
                    = new SelectClause(clonedParentQuerySourceReferenceExpression);

                var lastResultOperator = ProcessResultOperators(clonedParentQueryModel);

                clonedParentQueryModel.ResultTypeOverride
                    = typeof(IQueryable<>).MakeGenericType(clonedParentQuerySourceReferenceExpression.Type);

                var joinQuerySourceReferenceExpression
                    = CreateJoinToParentQuery(
                        clonedParentQueryModel,
                        clonedParentQuerySource,
                        collectionQuerySourceReferenceExpression,
                        navigation.ForeignKey,
                        collectionQueryModel);

                ApplyParentOrderings(
                    parentOrderings,
                    clonedParentQueryModel,
                    parentQuerySource,
                    clonedParentQuerySourceReferenceExpression,
                    lastResultOperator);

                LiftOrderBy(
                    clonedParentQuerySource,
                    joinQuerySourceReferenceExpression,
                    clonedParentQueryModel,
                    collectionQueryModel);

                return collectionQueryModel;
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
                        && (querySource = (node.Left as QuerySourceReferenceExpression)?.ReferencedQuerySource) != null)
                    {
                        _querySources.Add(querySource);

                        if ((querySource = (node.Right as QuerySourceReferenceExpression)?.ReferencedQuerySource) != null)
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
                IQuerySource querySource,
                Expression outerTargetExpression,
                IForeignKey foreignKey,
                QueryModel targetQueryModel)
            {
                var subQueryExpression = new SubQueryExpression(parentQueryModel);

                var joinClause
                    = new JoinClause(
                        "_" + querySource.ItemName,
                        querySource.ItemType,
                        subQueryExpression,
                        CreateKeyAccessExpression(
                            outerTargetExpression,
                            foreignKey.Properties),
                        Expression.Constant(null));

                var joinQuerySourceReferenceExpression = new QuerySourceReferenceExpression(joinClause);

                joinClause.InnerKeySelector
                    = CreateKeyAccessExpression(
                        joinQuerySourceReferenceExpression,
                        foreignKey.PrincipalKey.Properties);

                targetQueryModel.BodyClauses.Add(joinClause);

                return joinQuerySourceReferenceExpression;
            }

            // TODO: Unify this with other versions
            private static Expression CreateKeyAccessExpression(Expression target, IReadOnlyList<IProperty> properties)
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
                IEnumerable<Ordering> orderings,
                QueryModel queryModel,
                IQuerySource querySource,
                Expression targetExpression,
                bool reverseOrdering)
            {
                var orderByClause = queryModel.BodyClauses.OfType<OrderByClause>().LastOrDefault();

                if (orderByClause == null)
                {
                    queryModel.BodyClauses.Add(orderByClause = new OrderByClause());
                }

                var querySourceMapping = new QuerySourceMapping();

                querySourceMapping.AddMapping(querySource, targetExpression);

                foreach (var ordering in orderings)
                {
                    orderByClause.Orderings
                        .Add(
                            new Ordering(
                                CloningExpressionVisitor
                                    .AdjustExpressionAfterCloning(ordering.Expression, querySourceMapping),
                                ordering.OrderingDirection));
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
                QueryModel toQueryModel)
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
                                    CloningExpressionVisitor
                                        .AdjustExpressionAfterCloning(ordering.Expression, querySourceMapping),
                                    ordering.OrderingDirection));
                    }

                    toQueryModel.BodyClauses.Add(outerOrderByClause);

                    if (canRemove)
                    {
                        fromQueryModel.BodyClauses.Remove(orderByClause);
                    }
                }
            }

            private static void CollectParentOrderings(
                QueryModel queryModel,
                INavigation navigation,
                Expression expression,
                ICollection<Ordering> parentOrderings)
            {
                var orderings = parentOrderings;

                var orderByClause = queryModel.BodyClauses.OfType<OrderByClause>().LastOrDefault();

                if (orderByClause != null)
                {
                    orderings = orderings.Concat(orderByClause.Orderings).ToArray();
                }

                foreach (var property in navigation.ForeignKey.PrincipalKey.Properties)
                {
                    if (!ContainsOrdering(orderings, expression, property))
                    {
                        parentOrderings.Add(
                            new Ordering(
                                EntityQueryModelVisitor.CreatePropertyExpression(expression, property),
                                OrderingDirection.Asc));
                    }
                }
            }

            private static bool ContainsOrdering(
                IEnumerable<Ordering> orderings,
                Expression expression,
                IPropertyBase property)
            {
                foreach (var ordering in orderings)
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

            private Expression BuildReferenceIncludeExpressions(
                Expression targetEntityExpression,
                bool trackingQuery,
                ICollection<Expression> propertyExpressions,
                Expression lastPropertyExpression,
                ref int includedIndex)
            {
                propertyExpressions.Add(
                    lastPropertyExpression
                        = EntityQueryModelVisitor.CreatePropertyExpression(
                            lastPropertyExpression, Navigation));

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
                            .BuildReferenceIncludeExpressions(
                                relatedEntityExpression,
                                trackingQuery,
                                propertyExpressions,
                                lastPropertyExpression,
                                ref includedIndex));
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
