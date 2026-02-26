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

namespace AxOS.Kernel
{
    public sealed class SystemMetabolism
    {
        public float MaxCapacity { get; private set; } = 1000.0f;
        public float CurrentEnergyBudget { get; private set; } = 1000.0f;
        public float FatigueThreshold { get; private set; } = 280.0f;
        public float ZombieActivationThreshold { get; private set; } = 200.0f;
        public float FatigueRemainingRatio { get; private set; } = 0.28f;
        public float ZombieActivationRatio { get; private set; } = 0.20f;
        public float ZombieCriticThreshold { get; private set; } = 0.95f;
        public float ZombieThreshold => ZombieCriticThreshold;
        public bool ZombieModeActive { get; private set; }

        public float EnergyPercent => MaxCapacity <= 0.0f ? 0.0f : CurrentEnergyBudget / MaxCapacity;
        public bool IsExhausted => CurrentEnergyBudget <= 0.0001f;
        public bool CanDeepThink => CurrentEnergyBudget > FatigueThreshold && !ZombieModeActive;
        public float CriticThreshold => ZombieModeActive ? ZombieCriticThreshold : 0.50f;

        public void Configure(float maxCapacity, float fatigueThreshold, float zombieThreshold)
        {
            float safeMax = maxCapacity <= 0.0f ? 1000.0f : maxCapacity;
            float safeFatigue = fatigueThreshold < 0.0f ? 0.0f : fatigueThreshold;
            if (safeFatigue >= safeMax)
            {
                safeFatigue = safeMax * 0.5f;
            }

            float fatigueRatio = Clamp(safeFatigue / safeMax, 0.01f, 0.95f);
            float zombieActivationRatio = fatigueRatio < 0.20f ? fatigueRatio : 0.20f;
            ConfigureRelative(safeMax, fatigueRatio, zombieActivationRatio, zombieThreshold);
        }

        public void ConfigureRelative(
            float maxCapacity,
            float fatigueRemainingRatio,
            float zombieActivationRatio,
            float zombieCriticThreshold)
        {
            float safeMax = maxCapacity <= 0.0f ? 1000.0f : maxCapacity;
            float safeFatigueRatio = Clamp(fatigueRemainingRatio, 0.01f, 0.95f);
            float safeZombieActivationRatio = Clamp(zombieActivationRatio, 0.01f, safeFatigueRatio);

            MaxCapacity = safeMax;
            FatigueRemainingRatio = safeFatigueRatio;
            ZombieActivationRatio = safeZombieActivationRatio;
            ZombieCriticThreshold = Clamp01(zombieCriticThreshold <= 0.0f ? 0.95f : zombieCriticThreshold);

            FatigueThreshold = MaxCapacity * FatigueRemainingRatio;
            ZombieActivationThreshold = MaxCapacity * ZombieActivationRatio;
            CurrentEnergyBudget = MaxCapacity;
            ZombieModeActive = false;
        }

        public void RescaleMaxCapacity(float newMaxCapacity, bool preserveEnergyPercent)
        {
            float safeMax = newMaxCapacity <= 0.0f ? MaxCapacity : newMaxCapacity;
            float previousPercent = EnergyPercent;

            MaxCapacity = safeMax;
            FatigueThreshold = MaxCapacity * FatigueRemainingRatio;
            ZombieActivationThreshold = MaxCapacity * ZombieActivationRatio;

            if (preserveEnergyPercent)
            {
                CurrentEnergyBudget = MaxCapacity * Clamp01(previousPercent);
            }
            else if (CurrentEnergyBudget > MaxCapacity)
            {
                CurrentEnergyBudget = MaxCapacity;
            }

            if (CurrentEnergyBudget <= ZombieActivationThreshold)
            {
                ZombieModeActive = true;
            }
        }

        public void Consume(float amount)
        {
            if (amount <= 0.0f)
            {
                return;
            }

            CurrentEnergyBudget -= amount;
            if (CurrentEnergyBudget < 0.0f)
            {
                CurrentEnergyBudget = 0.0f;
            }

            if (CurrentEnergyBudget <= ZombieActivationThreshold)
            {
                ZombieModeActive = true;
            }
        }

        public void Recharge(float amount = -1.0f)
        {
            if (amount <= 0.0f)
            {
                CurrentEnergyBudget = MaxCapacity;
            }
            else
            {
                CurrentEnergyBudget += amount;
                if (CurrentEnergyBudget > MaxCapacity)
                {
                    CurrentEnergyBudget = MaxCapacity;
                }
            }

            ZombieModeActive = false;
        }

        public void TriggerZombieMode()
        {
            ZombieModeActive = true;
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }
            if (value > 1.0f)
            {
                return 1.0f;
            }
            return value;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }
}

