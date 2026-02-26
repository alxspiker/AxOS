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
using System.Text;

namespace AxOS.Core
{
    public sealed class Tensor
    {
        public float[] Data { get; private set; }
        public Shape Shape { get; private set; }

        public Tensor()
        {
            Data = Array.Empty<float>();
            Shape = new Shape();
        }

        public Tensor(float value)
        {
            Data = new[] { value };
            Shape = new Shape(1);
        }

        public Tensor(float[] values)
        {
            Data = values == null ? Array.Empty<float>() : (float[])values.Clone();
            Shape = new Shape(Data.Length);
        }

        public Tensor(Shape shape, float fill = 0.0f)
        {
            Shape = shape?.Clone() ?? new Shape();
            int total = Shape.TotalElements;
            Data = new float[total];
            Fill(fill);
        }

        public int NDim => Shape.NDim;
        public int Total => Data.Length;
        public bool IsEmpty => Data.Length == 0;

        public float this[int i]
        {
            get => Data[i];
            set => Data[i] = value;
        }

        public Tensor Copy()
        {
            Tensor t = new Tensor
            {
                Data = (float[])Data.Clone(),
                Shape = Shape.Clone()
            };
            return t;
        }

        public Tensor Reshape(Shape newShape)
        {
            if (newShape == null)
            {
                throw new ArgumentNullException(nameof(newShape));
            }

            if (newShape.TotalElements != Total)
            {
                throw new InvalidOperationException("Reshape size mismatch.");
            }

            Tensor t = new Tensor
            {
                Data = (float[])Data.Clone(),
                Shape = newShape.Clone()
            };
            return t;
        }

        public Tensor Flatten()
        {
            return Reshape(new Shape(Total));
        }

        public void Fill(float value)
        {
            for (int i = 0; i < Data.Length; i++)
            {
                Data[i] = value;
            }
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Tensor");
            sb.Append(Shape.ToString());
            if (Data.Length <= 20)
            {
                sb.Append(" [");
                for (int i = 0; i < Data.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(Data[i]);
                }
                sb.Append(']');
            }
            return sb.ToString();
        }

        public static Tensor Zeros(Shape shape) => new Tensor(shape, 0.0f);
        public static Tensor Ones(Shape shape) => new Tensor(shape, 1.0f);
    }
}

