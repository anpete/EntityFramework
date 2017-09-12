// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Metadata;
using Remotion.Linq;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Clauses.ResultOperators;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.Internal
{
    public partial class IncludeCompiler
    {
        private abstract class IncludeLoadTreeNodeBase
        {
            protected static void AddLoadPath(
                IncludeLoadTreeNodeBase node,
                IReadOnlyList<INavigation> navigationPath,
                int index)
            {
                while (index < navigationPath.Count)
                {
                    var navigation = navigationPath[index];
                    var childNode = node.Children.SingleOrDefault(n => n.Navigation == navigation);

                    if (childNode == null)
                    {
                        node.Children.Add(childNode = new IncludeLoadTreeNode(navigation));
                    }

                    node = childNode;
                    index = index + 1;
                }
            }

            protected ICollection<IncludeLoadTreeNode> Children { get; } = new List<IncludeLoadTreeNode>();

            protected void Compile(
                QueryCompilationContext queryCompilationContext,
                QueryModel queryModel,
                bool trackingQuery,
                bool asyncQuery,
                ref int collectionIncludeId,
                QuerySourceReferenceExpression targetQuerySourceReferenceExpression,
                MemberInitExpression userMaterializationExpression)
            {
                var entityParameter
                    = Expression.Parameter(targetQuerySourceReferenceExpression.Type, name: "entity");

                var propertyExpressions = new List<Expression>();
                var blockExpressions = new List<Expression>();

                var track = trackingQuery && userMaterializationExpression == null;

                if (track)
                {
                    blockExpressions.Add(
                        Expression.Call(
                            Expression.Property(
                                EntityQueryModelVisitor.QueryContextParameter,
                                nameof(QueryContext.QueryBuffer)),
                            _queryBufferStartTrackingMethodInfo,
                            entityParameter,
                            Expression.Constant(
                                queryCompilationContext.FindEntityType(targetQuerySourceReferenceExpression.ReferencedQuerySource)
                                ?? queryCompilationContext.Model.FindEntityType(entityParameter.Type))));
                }

                var includedIndex = 0;

                // ReSharper disable once LoopCanBeConvertedToQuery
                foreach (var includeLoadTreeNode in Children)
                {
                    blockExpressions.Add(
                        includeLoadTreeNode.Compile(
                            queryCompilationContext,
                            targetQuerySourceReferenceExpression,
                            entityParameter,
                            propertyExpressions,
                            track,
                            asyncQuery,
                            ref includedIndex,
                            ref collectionIncludeId));
                }

                if (blockExpressions.Count > 1
                    || blockExpressions.Count == 1
                    && !track)
                {
                    AwaitTaskExpressions(asyncQuery, blockExpressions);

                    var targetExpression
                        = userMaterializationExpression 
                            ?? (Expression)targetQuerySourceReferenceExpression;

                    var includeExpression
                        = blockExpressions.Last().Type == typeof(Task)
                            ? (Expression)Expression.Property(
                                Expression.Call(
                                    _includeAsyncMethodInfo
                                        .MakeGenericMethod(targetQuerySourceReferenceExpression.Type),
                                    EntityQueryModelVisitor.QueryContextParameter,
                                    targetExpression,
                                    Expression.NewArrayInit(typeof(object), propertyExpressions),
                                    Expression.Lambda(
                                        Expression.Block(blockExpressions),
                                        EntityQueryModelVisitor.QueryContextParameter,
                                        entityParameter,
                                        _includedParameter,
                                        _cancellationTokenParameter),
                                    _cancellationTokenParameter),
                                nameof(Task<object>.Result))
                            : Expression.Call(
                                _includeMethodInfo.MakeGenericMethod(targetQuerySourceReferenceExpression.Type),
                                EntityQueryModelVisitor.QueryContextParameter,
                                targetExpression,
                                Expression.NewArrayInit(typeof(object), propertyExpressions),
                                Expression.Lambda(
                                    Expression.Block(typeof(void), blockExpressions),
                                    EntityQueryModelVisitor.QueryContextParameter,
                                    entityParameter,
                                    _includedParameter));

                    ApplyIncludeExpressionsToQueryModel(queryModel, targetExpression, includeExpression);
                }
            }

            protected static void AwaitTaskExpressions(bool asyncQuery, List<Expression> blockExpressions)
            {
                if (asyncQuery)
                {
                    var taskExpressions = new List<Expression>();

                    foreach (var expression in blockExpressions.ToArray())
                    {
                        if (expression.Type == typeof(Task))
                        {
                            blockExpressions.Remove(expression);
                            taskExpressions.Add(expression);
                        }
                    }

                    if (taskExpressions.Count > 0)
                    {
                        blockExpressions.Add(
                            taskExpressions.Count == 1
                                ? taskExpressions[0]
                                : Expression.Call(
                                    _awaitManyMethodInfo,
                                    Expression.NewArrayInit(
                                        typeof(Func<Task>),
                                        taskExpressions.Select(e => Expression.Lambda(e)))));
                    }
                }
            }

            private static readonly MethodInfo _awaitManyMethodInfo
                = typeof(IncludeLoadTreeNodeBase).GetTypeInfo()
                    .GetDeclaredMethod(nameof(_AwaitMany));

            // ReSharper disable once InconsistentNaming

            private static async Task _AwaitMany(IReadOnlyList<Func<Task>> taskFactories)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < taskFactories.Count; i++)
                {
                    await taskFactories[i]();
                }
            }

            protected static void ApplyIncludeExpressionsToQueryModel(
                QueryModel queryModel,
                Expression expression,
                Expression includeExpression)
            {
                var includeReplacingExpressionVisitor = new IncludeReplacingExpressionVisitor();

                foreach (var groupResultOperator
                    in queryModel.ResultOperators.OfType<GroupResultOperator>())
                {
                    var newElementSelector
                        = includeReplacingExpressionVisitor.Replace(
                            expression,
                            includeExpression,
                            groupResultOperator.ElementSelector);

                    if (!ReferenceEquals(newElementSelector, groupResultOperator.ElementSelector))
                    {
                        groupResultOperator.ElementSelector = newElementSelector;

                        return;
                    }
                }

                queryModel.SelectClause.TransformExpressions(
                    e => includeReplacingExpressionVisitor.Replace(
                        expression,
                        includeExpression,
                        e));
            }

            private class IncludeReplacingExpressionVisitor : RelinqExpressionVisitor
            {
                private Expression _expression;
                private Expression _includeExpression;

                public Expression Replace(
                    Expression expression,
                    Expression includeExpression,
                    Expression searchedExpression)
                {
                    _expression = expression;
                    _includeExpression = includeExpression;

                    return Visit(searchedExpression);
                }

                public override Expression Visit(Expression expression)
                {
                    if (_expression == null)
                    {
                        return expression;
                    }

                    if (ReferenceEquals(expression, _expression))
                    {
                        _expression = null;

                        return _includeExpression;
                    }

                    return base.Visit(expression);
                }
            }
        }
    }
}
