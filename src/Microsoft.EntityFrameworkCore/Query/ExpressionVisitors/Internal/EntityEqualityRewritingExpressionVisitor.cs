// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Utilities;
using Remotion.Linq;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Microsoft.EntityFrameworkCore.Query.ExpressionVisitors.Internal
{
    public class EntityEqualityRewritingExpressionVisitor : RelinqExpressionVisitor
    {
        private readonly IModel _model;

        public EntityEqualityRewritingExpressionVisitor([NotNull] IModel model)
        {
            Check.NotNull(model, nameof(model));

            _model = model;
        }

        public virtual void Rewrite([NotNull] QueryModel queryModel)
        {
            Check.NotNull(queryModel, nameof(queryModel));

            queryModel.TransformExpressions(Visit);
        }

        protected override Expression VisitBinary(BinaryExpression binaryExpression)
        {
            Check.NotNull(binaryExpression, nameof(binaryExpression));

            var newExpression = base.VisitBinary(binaryExpression);

            binaryExpression = newExpression as BinaryExpression;

            if (binaryExpression?.NodeType == ExpressionType.Equal
                || binaryExpression?.NodeType == ExpressionType.NotEqual)
            {
                var leftEntityType = _model.FindEntityType(binaryExpression.Left.Type);
                var rightEntityType = _model.FindEntityType(binaryExpression.Right.Type);

                Expression leftExpression = null;
                Expression rightExpression = null;

                if (leftEntityType != null)
                {
                    leftExpression
                        = CreateKeyAccessExpression(
                            binaryExpression.Left,
                            leftEntityType.FindPrimaryKey().Properties);

                    if (rightEntityType == null)
                    {
                        var constantExpression = binaryExpression.Right as ConstantExpression;

                        if (constantExpression != null
                            && constantExpression.Value == null)
                        {
                            rightExpression
                                = IsCompositeKey(leftExpression.Type)
                                    ? CreateNullCompositeKey(leftExpression)
                                    : binaryExpression.Right;
                        }
                    }
                }

                if (rightEntityType != null)
                {
                    rightExpression
                        = CreateKeyAccessExpression(
                            binaryExpression.Right,
                            rightEntityType.FindPrimaryKey().Properties);

                    if (leftEntityType == null)
                    {
                        var constantExpression = binaryExpression.Left as ConstantExpression;

                        if (constantExpression != null
                            && constantExpression.Value == null)
                        {
                            leftExpression
                                = IsCompositeKey(rightExpression.Type)
                                    ? CreateNullCompositeKey(rightExpression)
                                    : binaryExpression.Left;
                        }
                    }
                }

                if (leftExpression != null
                    && rightExpression != null)
                {
                    return Expression.MakeBinary(binaryExpression.NodeType, leftExpression, rightExpression);
                }
            }

            return newExpression;
        }

        private static Expression CreateKeyAccessExpression(Expression target, IReadOnlyList<IProperty> properties)
        {
            return properties.Count == 1
                ? CreatePropertyExpression(target, properties[0])
                : Expression.New(
                    _compositeKeyCtor,
                    Expression.NewArrayInit(
                        typeof(object),
                        properties
                            .Select(p => Expression.Convert(CreatePropertyExpression(target, p), typeof(object)))
                            .Cast<Expression>()
                            .ToArray()));
        }

        private static readonly MethodInfo _efPropertyMethod
            = typeof(EF).GetTypeInfo().GetDeclaredMethod(nameof(EF.Property));

        private static Expression CreatePropertyExpression(Expression target, IProperty property)
            => Expression.Call(
                null,
                _efPropertyMethod.MakeGenericMethod(property.ClrType),
                target,
                Expression.Constant(property.Name));

        private static readonly ConstructorInfo _compositeKeyCtor
            = typeof(CompositeKey).GetTypeInfo().DeclaredConstructors.Single();

        private static NewExpression CreateNullCompositeKey(Expression otherExpression)
            => Expression.New(
                _compositeKeyCtor,
                Expression.NewArrayInit(
                    typeof(object),
                    Enumerable.Repeat(
                        Expression.Constant(null),
                        ((NewArrayExpression)((NewExpression)otherExpression).Arguments.Single()).Expressions.Count)));

        public static bool IsCompositeKey([NotNull] Type type)
        {
            Check.NotNull(type, nameof(type));

            return type == typeof(CompositeKey);
        }

        private struct CompositeKey
        {
            public static bool operator ==(CompositeKey x, CompositeKey y) => x.Equals(y);
            public static bool operator !=(CompositeKey x, CompositeKey y) => !x.Equals(y);

            private readonly object[] _values;

            [UsedImplicitly]
            public CompositeKey(object[] values)
            {
                _values = values;
            }

            public override bool Equals(object obj)
                => _values.SequenceEqual(((CompositeKey)obj)._values);

            public override int GetHashCode() => 0;
        }

        protected override Expression VisitSubQuery(SubQueryExpression subQueryExpression)
        {
            Check.NotNull(subQueryExpression, nameof(subQueryExpression));

            subQueryExpression.QueryModel.TransformExpressions(Visit);

            return subQueryExpression;
        }
    }
}
