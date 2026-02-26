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
using System.Collections.Generic;

namespace AxOS.Kernel
{
    public sealed class BatchController
    {
        public sealed class BatchRunResult
        {
            public int Processed;
            public int Succeeded;
            public int ReflexHits;
            public int DeepThinkHits;
            public int ZombieEvents;
            public int SleepCycles;
            public int Failures;
        }

        private readonly Queue<DataStream> _pending = new Queue<DataStream>();

        public int PendingCount => _pending.Count;

        public void Enqueue(DataStream item)
        {
            if (item == null)
            {
                return;
            }

            _pending.Enqueue(item);
        }

        public void Clear()
        {
            _pending.Clear();
        }

        public BatchRunResult Run(KernelLoop kernelLoop, int maxItems)
        {
            BatchRunResult summary = new BatchRunResult();
            if (kernelLoop == null)
            {
                return summary;
            }

            int budget = maxItems <= 0 ? int.MaxValue : maxItems;
            while (_pending.Count > 0 && summary.Processed < budget)
            {
                DataStream item = _pending.Dequeue();
                IngestResult result = kernelLoop.ProcessIngestPipeline(item);
                summary.Processed++;

                if (result.Success)
                {
                    summary.Succeeded++;
                }
                else
                {
                    summary.Failures++;
                }

                if (result.ReflexHit)
                {
                    summary.ReflexHits++;
                }
                if (result.DeepThinkPath)
                {
                    summary.DeepThinkHits++;
                }
                if (result.ZombieTriggered)
                {
                    summary.ZombieEvents++;
                }
                if (result.SleepTriggered)
                {
                    summary.SleepCycles++;
                }
            }

            return summary;
        }
    }
}

