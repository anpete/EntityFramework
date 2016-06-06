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
    ///     Represents a SQL LIKE expression.
    /// </summary>
    public class LikeExpression : Expression
    {
        /// <summary>
        ///     Creates a new instance of LikeExpression.
        /// </summary>
        /// <param name="match"> The expression to match. </param>
        /// <param name="pattern"> The pattern to match. </param>
        public LikeExpression([NotNull] Expression match, [NotNull] Expression pattern)
        {
            Check.NotNull(match, nameof(match));
            Check.NotNull(pattern, nameof(pattern));

            Match = match;
            Pattern = pattern;
        }

        /// <summary>
        ///     Gets the match expression.
        /// </summary>
        /// <value>
        ///     The match expression.
        /// </value>
        public virtual Expression Match { get; }

        /// <summary>
        ///     Gets the pattern to match.
        /// </summary>
        /// <value>
        ///     The pattern to match.
        /// </value>
        public virtual Expression Pattern { get; }

        public override ExpressionType NodeType => ExpressionType.Extension;

        public override Type Type => typeof(bool);

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitLike(this)
                : base.Accept(visitor);
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            var newMatchExpression = visitor.Visit(Match);
            var newPatternExpression = visitor.Visit(Pattern);

            return (newMatchExpression != Match)
                   || (newPatternExpression != Pattern)
                ? new LikeExpression(newMatchExpression, newPatternExpression)
                : this;
        }

        public override string ToString() => Match + " LIKE " + Pattern;
    }
}
