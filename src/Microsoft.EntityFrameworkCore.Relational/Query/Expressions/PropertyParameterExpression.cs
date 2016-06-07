// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query.Sql;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.Expressions
{
    /// <summary>
    ///     An expression that represents accessing a property on a query parameter.
    /// </summary>
    public class PropertyParameterExpression : Expression
    {
        /// <summary>
        ///     Creates a new instance of a PropertyParameterExpression.
        /// </summary>
        /// <param name="name"> The parameter name. </param>
        /// <param name="property"> The property to access. </param>
        public PropertyParameterExpression([NotNull] string name, [NotNull] IProperty property)
        {
            Check.NotEmpty(name, nameof(name));
            Check.NotNull(property, nameof(property));

            Name = name;
            Property = property;
        }

        /// <summary>
        ///     Gets the parameter name.
        /// </summary>
        /// <value>
        ///     The parameter name.
        /// </value>
        public virtual string Name { get; }

#pragma warning disable 108

        /// <summary>
        ///     Gets the property.
        /// </summary>
        /// <value>
        ///     The property.
        /// </value>
        public virtual IProperty Property { get; }
#pragma warning restore 108

        /// <summary>
        ///     Name of the property parameter when used in DbCommands.
        /// </summary>
        public virtual string PropertyParameterName => $"{Name}_{Property.Name}";

        /// <summary>
        /// Returns the node type of this <see cref="Expression" />. (Inherited from <see cref="Expression" />.)
        /// </summary>
        /// <returns>The <see cref="ExpressionType"/> that represents this expression.</returns>
        public override ExpressionType NodeType => ExpressionType.Extension;
        
        /// <summary>
        /// Gets the static type of the expression that this <see cref="Expression" /> represents. (Inherited from <see cref="Expression"/>.)
        /// </summary>
        /// <returns>The <see cref="Type"/> that represents the static type of the expression.</returns>
        public override Type Type => Property.ClrType;

        public override string ToString() => Name;

        protected override Expression VisitChildren(ExpressionVisitor visitor) => this;

        protected override Expression Accept(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            var specificVisitor = visitor as ISqlExpressionVisitor;

            return specificVisitor != null
                ? specificVisitor.VisitPropertyParameter(this)
                : base.Accept(visitor);
        }
    }
}
