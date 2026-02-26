// Copyright (c) 2025-2026 alxspiker. All rights reserved.
// Licensed under the GNU Affero General Public License v3.0 (AGPL-3.0)
// See LICENSE file in the project root for full license text.
using System;
using AxOS.Core;
using AxOS.Brain;
using AxOS.Kernel;
using AxOS.Hardware;
using AxOS.Storage;
using AxOS.Diagnostics;

namespace AxOS.Core
{
    public sealed class Shape
    {
        public int[] Dims { get; private set; }

        public Shape(params int[] dims)
        {
            Dims = dims ?? Array.Empty<int>();
        }

        public int NDim => Dims.Length;

        public int TotalElements
        {
            get
            {
                if (Dims.Length == 0)
                {
                    return 0;
                }

                long total = 1;
                for (int i = 0; i < Dims.Length; i++)
                {
                    if (Dims[i] < 0)
                    {
                        throw new InvalidOperationException("Shape cannot contain negative dimensions.");
                    }
                    total *= Dims[i];
                    if (total > int.MaxValue)
                    {
                        throw new OverflowException("Tensor size overflow.");
                    }
                }

                return (int)total;
            }
        }

        public int this[int index]
        {
            get => Dims[index];
            set => Dims[index] = value;
        }

        public Shape Clone()
        {
            int[] copy = new int[Dims.Length];
            Array.Copy(Dims, copy, Dims.Length);
            return new Shape(copy);
        }

        public override string ToString()
        {
            if (Dims.Length == 0)
            {
                return "()";
            }

            string s = "(";
            for (int i = 0; i < Dims.Length; i++)
            {
                if (i > 0)
                {
                    s += ", ";
                }
                s += Dims[i].ToString();
            }
            s += ")";
            return s;
        }

        public override bool Equals(object obj)
        {
            if (obj is not Shape other || other.Dims.Length != Dims.Length)
            {
                return false;
            }

            for (int i = 0; i < Dims.Length; i++)
            {
                if (Dims[i] != other.Dims[i])
                {
                    return false;
                }
            }

            return true;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            for (int i = 0; i < Dims.Length; i++)
            {
                hash = (hash * 31) ^ Dims[i];
            }
            return hash;
        }
    }
}

