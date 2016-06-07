// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Utilities;
using Remotion.Linq.Clauses;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    public abstract class TableExpressionBase : Expression
    {
        private string _alias;
        private IQuerySource _querySource;

        protected TableExpressionBase(
            [CanBeNull] IQuerySource querySource, [CanBeNull] string alias)
        {
            _querySource = querySource;
            _alias = alias;
        }

        /// <summary>
        /// Returns the node type of this <see cref="Expression" />. (Inherited from <see cref="Expression" />.)
        /// </summary>
        /// <returns>The <see cref="ExpressionType"/> that represents this expression.</returns>
        public override ExpressionType NodeType => ExpressionType.Extension;
        
        /// <summary>
        /// Gets the static type of the expression that this <see cref="Expression" /> represents. (Inherited from <see cref="Expression"/>.)
        /// </summary>
        /// <returns>The <see cref="Type"/> that represents the static type of the expression.</returns>
        public override Type Type => typeof(object);

        public virtual IQuerySource QuerySource
        {
            get { return _querySource; }
            [param: NotNull]
            set
            {
                Check.NotNull(value, nameof(value));

                _querySource = value;
            }
        }

        public virtual string Alias
        {
            get { return _alias; }
            [param: NotNull]
            set
            {
                Check.NotNull(value, nameof(value));

                _alias = value;
            }
        }

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;
    }
}
