// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using JetBrains.Annotations;

namespace Microsoft.EntityFrameworkCore.Metadata
{
    /// <summary>
    ///     <para>
    ///         Represents a view in an <see cref="IMutableModel" />.
    ///     </para>
    ///     <para>
    ///         This interface is used during model creation and allows the metadata to be modified.
    ///         Once the model is built, <see cref="IEntityType" /> represents a ready-only view of the same metadata.
    ///     </para>
    /// </summary>
    public interface IMutableViewType : IViewType, IMutableStructuralType
    {
        /// <summary>
        ///     Gets or sets the base type of the view. Returns null if this is not a derived type in an inheritance hierarchy.
        /// </summary>
        new IMutableViewType BaseType { get; [param: CanBeNull] set; }
    }
}
