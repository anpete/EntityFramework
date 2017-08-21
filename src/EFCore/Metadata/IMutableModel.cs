// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Microsoft.EntityFrameworkCore.Metadata
{
    /// <summary>
    ///     <para>
    ///         Metadata about the shape of entities, the relationships between them, and how they map to the database. A model is typically
    ///         created by overriding the <see cref="DbContext.OnConfiguring(DbContextOptionsBuilder)" /> method on a derived context, or
    ///         using <see cref="ModelBuilder" />.
    ///     </para>
    ///     <para>
    ///         This interface is used during model creation and allows the metadata to be modified.
    ///         Once the model is built, <see cref="IModel" /> represents a ready-only view of the same metadata.
    ///     </para>
    /// </summary>
    public interface IMutableModel : IModel, IMutableAnnotatable
    {
        /// <summary>
        ///     <para>
        ///         Adds a shadow state entity type to the model.
        ///     </para>
        ///     <para>
        ///         Shadow entities are not currently supported in a model that is used at runtime with a <see cref="DbContext" />.
        ///         Therefore, shadow state entity types will only exist in migration model snapshots, etc.
        ///     </para>
        /// </summary>
        /// <param name="name"> The name of the entity to be added. </param>
        /// <returns> The new entity type. </returns>
        IMutableEntityType AddEntityType([NotNull] string name);

        /// <summary>
        ///     Adds an entity type to the model.
        /// </summary>
        /// <param name="clrType"> The CLR class that is used to represent instances of this entity type. </param>
        /// <returns> The new entity type. </returns>
        IMutableEntityType AddEntityType([NotNull] Type clrType);

        /// <summary>
        ///     Adds an entity type with a defining navigation to the model.
        /// </summary>
        /// <param name="name"> The name of the entity to be added. </param>
        /// <param name="definingNavigationName"> The defining navigation. </param>
        /// <param name="definingEntityType"> The defining entity type. </param>
        /// <returns> The new entity type. </returns>
        IMutableEntityType AddEntityType(
            [NotNull] string name,
            [NotNull] string definingNavigationName,
            [NotNull] IMutableEntityType definingEntityType);

        /// <summary>
        ///     Adds an entity type with a defining navigation to the model.
        /// </summary>
        /// <param name="clrType"> The CLR class that is used to represent instances of this entity type. </param>
        /// <param name="definingNavigationName"> The defining navigation. </param>
        /// <param name="definingEntityType"> The defining entity type. </param>
        /// <returns> The new entity type. </returns>
        IMutableEntityType AddEntityType(
            [NotNull] Type clrType,
            [NotNull] string definingNavigationName,
            [NotNull] IMutableEntityType definingEntityType);

        /// <summary>
        ///     Gets the entity with the given name. Returns null if no entity type with the given name is found
        ///     or the entity type has a defining navigation.
        /// </summary>
        /// <param name="name"> The name of the entity type to find. </param>
        /// <returns> The entity type, or null if none are found. </returns>
        new IMutableEntityType FindEntityType([NotNull] string name);

        /// <summary>
        ///     Gets the entity type for the given name, defining navigation name
        ///     and the defining entity type. Returns null if no matching entity type is found.
        /// </summary>
        /// <param name="name"> The name of the entity type to find. </param>
        /// <param name="definingNavigationName"> The defining navigation of the entity type to find. </param>
        /// <param name="definingEntityType"> The defining entity type of the entity type to find. </param>
        /// <returns> The entity type, or null if none are found. </returns>
        IMutableEntityType FindEntityType(
            [NotNull] string name,
            [NotNull] string definingNavigationName,
            [NotNull] IMutableEntityType definingEntityType);

        /// <summary>
        ///     Removes an entity type without a defining navigation from the model.
        /// </summary>
        /// <param name="name"> The name of the entity type to be removed. </param>
        /// <returns> The entity type that was removed. </returns>
        IMutableEntityType RemoveEntityType([NotNull] string name);

        /// <summary>
        ///     Removes an entity type with a defining navigation from the model.
        /// </summary>
        /// <param name="name"> The name of the entity to be removed. </param>
        /// <param name="definingNavigationName"> The defining navigation. </param>
        /// <param name="definingEntityType"> The defining entity type. </param>
        /// <returns> The entity type that was removed. </returns>
        IMutableEntityType RemoveEntityType(
            [NotNull] string name,
            [NotNull] string definingNavigationName,
            [NotNull] IMutableEntityType definingEntityType);

        /// <summary>
        ///     Gets all entity types defined in the model.
        /// </summary>
        /// <returns> All entity types defined in the model. </returns>
        new IEnumerable<IMutableEntityType> GetEntityTypes();

        /// <summary>
        ///     <para>
        ///         Adds a shadow state view type to the model.
        ///     </para>
        ///     <para>
        ///         Shadow entities are not currently supported in a model that is used at runtime with a <see cref="DbContext" />.
        ///         Therefore, shadow state view types will only exist in migration model snapshots, etc.
        ///     </para>
        /// </summary>
        /// <param name="name"> The name of the view to be added. </param>
        /// <returns> The new view type. </returns>
        IMutableViewType AddViewType([NotNull] string name);

        /// <summary>
        ///     Adds an view type to the model.
        /// </summary>
        /// <param name="clrType"> The CLR class that is used to represent instances of this view type. </param>
        /// <returns> The new view type. </returns>
        IMutableViewType AddViewType([NotNull] Type clrType);

        /// <summary>
        ///     Gets the view with the given name. Returns null if no view type with the given name is found
        ///     or the view type has a defining navigation.
        /// </summary>
        /// <param name="name"> The name of the view type to find. </param>
        /// <returns> The view type, or null if none are found. </returns>
        new IMutableViewType FindViewType([NotNull] string name);

        /// <summary>
        ///     Removes an view type without a defining navigation from the model.
        /// </summary>
        /// <param name="name"> The name of the view type to be removed. </param>
        /// <returns> The view type that was removed. </returns>
        IMutableViewType RemoveViewType([NotNull] string name);

        /// <summary>
        ///     Gets all view types defined in the model.
        /// </summary>
        /// <returns> All view types defined in the model. </returns>
        new IEnumerable<IMutableViewType> GetViewTypes();
    }
}
