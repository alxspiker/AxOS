// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using IL2CPU.API.Attribs;

namespace AxOS.Hardware
{
    [Plug(Target = typeof(string))]
    internal static class StringCtorPlugs
    {
        [PlugMethod(Signature = "System_Void__System_String__ctor_System_ReadOnlySpan_1_System_Char__")]
        public static unsafe void Ctor(
            string aThis,
            ReadOnlySpan<char> value,
            [FieldAccess(Name = "System.String System.String.Empty")] ref string aStringEmpty,
            [FieldAccess(Name = "System.Int32 System.String._stringLength")] ref int aStringLength,
            [FieldAccess(Name = "System.Char System.String._firstChar")] char* aFirstChar)
        {
            aStringEmpty = "";
            aStringLength = value.Length;
            for (int i = 0; i < value.Length; i++)
            {
                aFirstChar[i] = value[i];
            }
        }
    }
}
