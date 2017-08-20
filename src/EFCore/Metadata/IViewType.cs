// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Microsoft.EntityFrameworkCore.Metadata
{
    /// <summary>
    ///     Represents a view type in an <see cref="IModel" />.
    /// </summary>
    public interface IViewType : IStructuralType
    {
        /// <summary>
        ///     Gets the base type of the view. Returns null if this is not a derived type in an inheritance hierarchy.
        /// </summary>
        new IViewType BaseType { get; }
    }
}
