// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     Represents a SQL IS NULL expression.
    /// </summary>
    public class IsNullExpression : Expression
    {
        private readonly Expression _operand;

        /// <summary>
        ///     Creates a new instance of IsNullExpression.
        /// </summary>
        /// <param name="operand"> The operand. </param>
        public IsNullExpression([NotNull] Expression operand)
        {
            _operand = operand;
        }

        /// <summary>
        ///     The operand.
        /// </summary>
        public virtual Expression Operand => _operand;

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => typeof(bool);

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitIsNull(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var newExpression = visitor.Visit(_operand);

            return newExpression != _operand
                ? new IsNullExpression(newExpression)
                : this;
        }

        public override string ToString() => $"{Operand} IS NULL";
    }
}
