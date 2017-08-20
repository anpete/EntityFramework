// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.EntityFrameworkCore.Metadata
{
    /// <summary>
    ///     Represents an entity type in an <see cref="IModel" />.
    /// </summary>
    public interface IEntityType : IStructuralType
    {
        /// <summary>
        ///     Gets the base type of the entity. Returns null if this is not a derived type in an inheritance hierarchy.
        /// </summary>
        new IEntityType BaseType { get; }

        /// <summary>
        ///     Gets the name of the defining navigation.
        /// </summary>
        string DefiningNavigationName { get; }

        /// <summary>
        ///     Gets the defining entity type.
        /// </summary>
        IEntityType DefiningEntityType { get; }

        /// <summary>
        ///     <para>
        ///         Gets primary key for this entity. Returns null if no primary key is defined.
        ///     </para>
        ///     <para>
        ///         To be a valid model, each entity type must have a primary key defined. Therefore, the primary key may be
        ///         null while the model is being created, but will be present by the time the model is used with a <see cref="DbContext" />.
        ///     </para>
        /// </summary>
        /// <returns> The primary key, or null if none is defined. </returns>
        IKey FindPrimaryKey();
    }
}
