// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Microsoft.EntityFrameworkCore.Metadata
{
    /// <summary>
    ///     Represents an structural type in an <see cref="IModel" />.
    /// </summary>
    public interface IStructuralType : IAnnotatable
    {
        /// <summary>
        ///     Gets the model this structural type belongs to.
        /// </summary>
        IModel Model { get; }

        /// <summary>
        ///     Gets the name of the structural type.
        /// </summary>
        string Name { get; }

        /// <summary>
        ///     <para>
        ///         Gets the CLR class that is used to represent instances of this structural type. Returns null if the structural type does not have a
        ///         corresponding CLR class (known as a shadow structural type).
        ///     </para>
        /// </summary>
        Type ClrType { get; }

        /// <summary>
        ///     <para>
        ///         Gets the property with a given name. Returns null if no property with the given name is defined.
        ///     </para>
        /// </summary>
        /// <param name="name"> The name of the property. </param>
        /// <returns> The property, or null if none is found. </returns>
        IProperty FindProperty([NotNull] string name);

        /// <summary>
        ///     <para>
        ///         Gets the properties defined on this structural type.
        ///     </para>
        /// </summary>
        /// <returns> The properties defined on this structural type. </returns>
        IEnumerable<IProperty> GetProperties();
    }
}
