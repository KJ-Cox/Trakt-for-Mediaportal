﻿using System;

namespace TraktAPI.Extensions
{
    public static class MathExtensions
    {
        public static int ToPercentage(this double? value)
        {
            if (value == null) return 0;
            return Convert.ToInt16(value * 10);
        }
    }
}
