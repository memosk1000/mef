﻿// -----------------------------------------------------------------------
// Copyright © Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.Internal;

namespace System.Composition.Hosting.Util
{
    static class Formatters
    {
        public static string ReadableList(IEnumerable<string> items)
        {
            Assumes.NotNull(items);

            string reply = string.Join(Properties.Resources.Formatter_ListSeparatorWithSpace, items.OrderBy(t => t));
            return !string.IsNullOrEmpty(reply) ? reply : Properties.Resources.Formatter_None;
        }

        public static string Format(Type type)
        {
            Assumes.NotNull(type);

            if (type.IsConstructedGenericType)
            {
                return FormatClosedGeneric(type);
            }
            return type.Name;
        }

        static string FormatClosedGeneric(Type closedGenericType)
        {
            Assumes.NotNull(closedGenericType);
            Assumes.IsTrue(closedGenericType.IsConstructedGenericType);

            var name = closedGenericType.Name.Substring(0, closedGenericType.Name.IndexOf("`"));
            var args = closedGenericType.GenericTypeArguments.Select(t => Format(t));
            return string.Format("{0}<{1}>", name, string.Join(", ", args));
        }
    }
}
