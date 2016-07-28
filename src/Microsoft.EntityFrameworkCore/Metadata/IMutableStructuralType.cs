// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace Microsoft.EntityFrameworkCore.Metadata
{
    public interface IMutableStructuralType : IStructuralType, IMutableAnnotatable
    {
        /// <summary>
        ///     Gets the model this structural type belongs to.
        /// </summary>
        new IMutableModel Model { get; }

        /// <summary>
        ///     Adds a property to this structural type.
        /// </summary>
        /// <param name="name"> The name of the property to add. </param>
        /// <param name="propertyType"> The type of value the property will hold. </param>
        /// <returns> The newly created property. </returns>
        IMutableProperty AddProperty([NotNull] string name, [NotNull] Type propertyType);

        /// <summary>
        ///     <para>
        ///         Gets the property with a given name. Returns null if no property with the given name is defined.
        ///     </para>
        /// </summary>
        /// <param name="name"> The name of the property. </param>
        /// <returns> The property, or null if none is found. </returns>
        new IMutableProperty FindProperty([NotNull] string name);

        /// <summary>
        ///     <para>
        ///         Gets the properties defined on this structural type.
        ///     </para>
        /// </summary>
        /// <returns> The properties defined on this structural type. </returns>
        new IEnumerable<IMutableProperty> GetProperties();

        /// <summary>
        ///     Removes a property from this structural type.
        /// </summary>
        /// <param name="name"> The name of the property to remove. </param>
        /// <returns> The property that was removed. </returns>
        IMutableProperty RemoveProperty([NotNull] string name);
    }
}
