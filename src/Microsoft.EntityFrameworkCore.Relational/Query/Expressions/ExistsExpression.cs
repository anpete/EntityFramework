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
    ///     Represents a SQL EXISTS expression.
    /// </summary>
    public class ExistsExpression : Expression
    {
        /// <summary>
        ///     Creates a new instance of a ExistsExpression..
        /// </summary>
        /// <param name="expression"> The subquery operand of the EXISTS expression. </param>
        public ExistsExpression([NotNull] Expression expression)
        {
            Check.NotNull(expression, nameof(expression));

            Expression = expression;
        }

        /// <summary>
        ///     Gets the subquery operand of the EXISTS expression.
        /// </summary>
        /// <value>
        ///     The subquery operand of the EXISTS expression.
        /// </value>
        public virtual Expression Expression { get; }

        /// <summary>
        /// Returns the node type of this <see cref="Expression" />. (Inherited from <see cref="Expression" />.)
        /// </summary>
        /// <returns>The <see cref="ExpressionType"/> that represents this expression.</returns>
        public override ExpressionType NodeType => ExpressionType.Extension;
        
        /// <summary>
        /// Gets the static type of the expression that this <see cref="Expression" /> represents. (Inherited from <see cref="Expression"/>.)
        /// </summary>
        /// <returns>The <see cref="Type"/> that represents the static type of the expression.</returns>
        public override Type Type => typeof(bool);

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitExists(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
    }
}
