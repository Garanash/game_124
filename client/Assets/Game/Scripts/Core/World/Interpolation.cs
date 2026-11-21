using System;
using System.Collections.Generic;

namespace Realmforge.Client.Core.World
{
    /// <summary>
    /// Tracks the server clock from timestamps in server messages. Entities are drawn slightly in the past
    /// (<see cref="InterpolationDelaySeconds"/>) so there are always two snapshots to blend between.
    /// </summary>
    public sealed class ServerClock
    {
        public const double InterpolationDelaySeconds = 0.1;
        private const double OffsetSmoothing = 0.1;

        private double _offset;
        private bool _synchronized;

        public double LocalTime { get; private set; }

        public double ServerTime => LocalTime + _offset;

        public double RenderTime => ServerTime - InterpolationDelaySeconds;

        public void Advance(double deltaSeconds)
        {
            LocalTime += deltaSeconds;
        }

        public void Observe(double serverTime)
        {
            double sample = serverTime - LocalTime;
            if (!_synchronized)
            {
                _offset = sample;
                _synchronized = true;
                return;
            }
            _offset += (sample - _offset) * OffsetSmoothing;
        }
    }

    public readonly struct EntityPose
    {
        public EntityPose(float x, float z, float heading)
        {
            X = x;
            Z = z;
            Heading = heading;
        }

        public float X { get; }
        public float Z { get; }
        public float Heading { get; }
    }

    public sealed class SnapshotBuffer
    {
        public const int Capacity = 32;

        private readonly List<(double Time, EntityPose Pose)> _samples = new List<(double, EntityPose)>(Capacity);

        public int Count => _samples.Count;

        public void Add(double serverTime, EntityPose pose)
        {
            if (_samples.Count > 0 && serverTime <= _samples[_samples.Count - 1].Time)
            {
                return;
            }
            if (_samples.Count == Capacity)
            {
                _samples.RemoveAt(0);
            }
            _samples.Add((serverTime, pose));
        }

        /// <summary>Blends the two samples around <paramref name="renderTime"/>; holds the edge sample outside the buffered range.</summary>
        public EntityPose Sample(double renderTime)
        {
            if (_samples.Count == 0)
            {
                return default;
            }
            if (renderTime <= _samples[0].Time)
            {
                return _samples[0].Pose;
            }
            for (int index = 1; index < _samples.Count; index++)
            {
                (double time, EntityPose pose) = _samples[index];
                if (renderTime > time)
                {
                    continue;
                }
                (double previousTime, EntityPose previous) = _samples[index - 1];
                float blend = (float)((renderTime - previousTime) / (time - previousTime));
                return new EntityPose(Lerp(previous.X, pose.X, blend), Lerp(previous.Z, pose.Z, blend), LerpAngle(previous.Heading, pose.Heading, blend));
            }
            return _samples[_samples.Count - 1].Pose;
        }

        private static float Lerp(float from, float to, float blend) => from + (to - from) * blend;

        private static float LerpAngle(float from, float to, float blend)
        {
            const float FullTurn = (float)(Math.PI * 2);
            float delta = (to - from) % FullTurn;
            if (delta > Math.PI)
            {
                delta -= FullTurn;
            }
            else if (delta < -Math.PI)
            {
                delta += FullTurn;
            }
            return from + delta * blend;
        }
    }
}
