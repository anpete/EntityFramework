// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Data.Entity.Storage;
using Microsoft.Data.Entity.Utilities;

namespace Microsoft.Data.Entity.Query.ExpressionVisitors.Internal
{
    public class QueryFlattener
    {
        private readonly MethodInfo _operatorToFlatten;
        private readonly RelationalQueryCompilationContext _relationalQueryCompilationContext;

        private readonly int _readerOffset;

        public QueryFlattener(
            [NotNull] RelationalQueryCompilationContext relationalQueryCompilationContext,
            [NotNull] MethodInfo operatorToFlatten,
            int readerOffset)
        {
            Check.NotNull(relationalQueryCompilationContext, nameof(relationalQueryCompilationContext));
            Check.NotNull(operatorToFlatten, nameof(operatorToFlatten));

            _relationalQueryCompilationContext = relationalQueryCompilationContext;
            _readerOffset = readerOffset;
            _operatorToFlatten = operatorToFlatten;
        }

        public virtual Expression Flatten([NotNull] MethodCallExpression methodCallExpression)
        {
            Check.NotNull(methodCallExpression, nameof(methodCallExpression));

            if (methodCallExpression.Method.MethodIsClosedFormOf(_operatorToFlatten))
            {
                var outerShapedQuery = methodCallExpression.Arguments[0];

                var outerShaper
                    = ((LambdaExpression)
                        ((MethodCallExpression)outerShapedQuery)
                            .Arguments[2])
                        .Body;

                var innerLambda
                    = methodCallExpression.Arguments[1] as LambdaExpression; // SelectMany case

                var innerShapedQuery
                    = innerLambda != null
                        ? (MethodCallExpression)innerLambda.Body
                        : (MethodCallExpression)methodCallExpression.Arguments[1];

                var innerShaper
                    = (MethodCallExpression)
                        ((LambdaExpression)
                            innerShapedQuery.Arguments[2]).Body;

                if (innerShaper.Arguments.Count > 3)
                {
                    // CreateEntity shaper, adjust the ValueBuffer offsets and allowNullResult

                    var newArguments = innerShaper.Arguments.ToList();

                    var oldBufferOffset
                        = (int)((ConstantExpression)innerShaper.Arguments[2]).Value;

                    var newBufferOffset = oldBufferOffset + _readerOffset;

                    newArguments[2] = Expression.Constant(newBufferOffset);

                    newArguments[7]
                        = new OffsettingExpressionVisitor(newBufferOffset)
                            .Visit(newArguments[7]);

                    newArguments[8] = Expression.Constant(true);

                    innerShaper = innerShaper.Update(innerShaper.Object, newArguments);
                }

                var resultSelector
                    = (MethodCallExpression)
                        ((LambdaExpression)methodCallExpression
                            .Arguments.Last())
                            .Body;

                if (_operatorToFlatten.Name != "_GroupJoin")
                {
                    var newResultSelector
                        = Expression.Lambda(
                            Expression.Call(resultSelector.Method, outerShaper, innerShaper),
                            (ParameterExpression)innerShaper.Arguments[1]);

                    return Expression.Call(
                        ((MethodCallExpression)outerShapedQuery).Method
                            .GetGenericMethodDefinition()
                            .MakeGenericMethod(newResultSelector.ReturnType),
                        ((MethodCallExpression)outerShapedQuery).Arguments[0],
                        ((MethodCallExpression)outerShapedQuery).Arguments[1],
                        newResultSelector);
                }

                var groupJoinMethod
                    = _relationalQueryCompilationContext.QueryMethodProvider
                        .GroupJoinMethod
                        .MakeGenericMethod(
                            outerShaper.Type,
                            innerShaper.Type,
                            ((LambdaExpression)methodCallExpression.Arguments[2]).ReturnType,
                            resultSelector.Type);

                var newShapedQueryMethod
                    = Expression.Call(
                        _relationalQueryCompilationContext.QueryMethodProvider
                            .QueryMethod,
                        ((MethodCallExpression)outerShapedQuery).Arguments[0],
                        ((MethodCallExpression)outerShapedQuery).Arguments[1],
                        Expression.Default(typeof(int?)));

                return
                    Expression.Call(
                        groupJoinMethod,
                        newShapedQueryMethod,
                        Expression
                            .Lambda(
                                outerShaper,
                                (ParameterExpression)innerShaper.Arguments[1]),
                        Expression
                            .Lambda(
                                innerShaper,
                                (ParameterExpression)innerShaper.Arguments[1]),
                        methodCallExpression.Arguments[3],
                        methodCallExpression.Arguments[4]);
            }

            return methodCallExpression;
        }

        private class OffsettingExpressionVisitor : ExpressionVisitor
        {
            private readonly int _offset;

            public OffsettingExpressionVisitor(int offset)
            {
                _offset = offset;
            }

            protected override Expression VisitMethodCall(MethodCallExpression methodCallExpression)
            {
                if (methodCallExpression.Method == ValueBuffer.IndexerGetMethod)
                {
                    var index = (int)((ConstantExpression)methodCallExpression.Arguments[0]).Value;

                    return methodCallExpression.Update(
                        methodCallExpression.Object,
                        new[]
                        {
                            Expression.Constant(_offset + index)
                        });
                }

                return base.VisitMethodCall(methodCallExpression);
            }
        }
    }
}
