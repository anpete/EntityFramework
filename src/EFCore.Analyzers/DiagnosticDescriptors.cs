// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.CodeAnalysis;

namespace Microsoft.AspNetCore.Mvc.Analyzers
{
    public static class DiagnosticDescriptors
    {
        public static readonly DiagnosticDescriptor EF1000_ApiActionsShouldReturnActionResultOf =
            new DiagnosticDescriptor(
                "MVC1002",
                "Actions on types annotated with ApiControllerAttribute should return ActionResult<T>.",
                "Actions on types annotated with ApiControllerAttribute should return ActionResult<T>.",
                "Usage",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);
    }
}
