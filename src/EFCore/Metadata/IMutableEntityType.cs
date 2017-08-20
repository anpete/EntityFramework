// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace Microsoft.EntityFrameworkCore.Metadata
{
    /// <summary>
    ///     <para>
    ///         Represents an entity in an <see cref="IMutableModel" />.
    ///     </para>
    ///     <para>
    ///         This interface is used during model creation and allows the metadata to be modified.
    ///         Once the model is built, <see cref="IEntityType" /> represents a ready-only view of the same metadata.
    ///     </para>
    /// </summary>
    public interface IMutableEntityType : IEntityType, IMutableStructuralType
    {
        /// <summary>
        ///     Gets or sets the base type of the entity. Returns null if this is not a derived type in an inheritance hierarchy.
        /// </summary>
        new IMutableEntityType BaseType { get; [param: CanBeNull] set; }

        /// <summary>
        ///     Sets the primary key for this entity.
        /// </summary>
        /// <param name="properties"> The properties that make up the primary key. </param>
        /// <returns> The newly created key. </returns>
        IMutableKey SetPrimaryKey([CanBeNull] IReadOnlyList<IMutableProperty> properties);

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
        new IMutableKey FindPrimaryKey();
    }
}
