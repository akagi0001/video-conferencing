﻿using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace PaderConference.Core.Extensions
{
    public static class NameValueCollectionExtensions
    {
        public static Dictionary<string, string> ToDictionary(this NameValueCollection nvc)
        {
            return nvc.AllKeys.ToDictionary(k => k!, k => nvc[k]!);
        }
    }
}
