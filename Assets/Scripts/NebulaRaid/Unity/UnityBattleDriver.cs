using NebulaRaid.Combat;
using UnityEngine;

namespace NebulaRaid.Unity
{
    /// <summary>
    /// Thin rendering adapter. Authoritative state remains in NebulaRaid.Core;
    /// Transform interpolation never feeds floating-point values back into combat.
    /// </summary>
    public sealed class UnityBattleDriver : MonoBehaviour
    {
        [SerializeField] private Transform[] actorViews = System.Array.Empty<Transform>();
        [SerializeField, Min(1)] private int actorsPerTeam = 4;
        [SerializeField, Min(1)] private int maximumCatchUpSteps = 4;
        [SerializeField] private float millimetersPerUnityUnit = 1000f;

        private FixedStepCombatSimulation? _simulation;
        private Vector3[] _previousPositions = System.Array.Empty<Vector3>();
        private Vector3[] _currentPositions = System.Array.Empty<Vector3>();
        private double _accumulatorSeconds;
        private double _stepSeconds;

        private void Awake()
        {
            BattleDefinition definition = SampleBattleFactory.CreateSkirmish(actorsPerTeam);
            _simulation = definition.CreateSimulation();
            _stepSeconds = 1.0 / definition.TickRate;
            int renderedActors = Mathf.Min(actorViews.Length, _simulation.ActorCount);
            _previousPositions = new Vector3[renderedActors];
            _currentPositions = new Vector3[renderedActors];
            ReadSimulationPositions(_currentPositions);
            System.Array.Copy(_currentPositions, _previousPositions, renderedActors);
        }

        private void Update()
        {
            if (_simulation == null)
            {
                return;
            }

            _accumulatorSeconds += Time.unscaledDeltaTime;
            int catchUpSteps = 0;
            while (_accumulatorSeconds >= _stepSeconds
                && catchUpSteps < maximumCatchUpSteps)
            {
                System.Array.Copy(
                    _currentPositions,
                    _previousPositions,
                    _currentPositions.Length);
                InputCommand[] commands = DeterministicBot.BuildCommands(_simulation);
                _simulation.Step(commands);
                ReadSimulationPositions(_currentPositions);
                _accumulatorSeconds -= _stepSeconds;
                catchUpSteps++;
            }

            if (catchUpSteps == maximumCatchUpSteps && _accumulatorSeconds >= _stepSeconds)
            {
                _accumulatorSeconds = _stepSeconds;
            }

            float alpha = (float)(_accumulatorSeconds / _stepSeconds);
            for (int i = 0; i < _currentPositions.Length; i++)
            {
                if (actorViews[i] != null)
                {
                    actorViews[i].position = Vector3.Lerp(
                        _previousPositions[i],
                        _currentPositions[i],
                        alpha);
                    actorViews[i].gameObject.SetActive(_simulation.GetActor(i).IsAlive);
                }
            }
        }

        private void ReadSimulationPositions(Vector3[] destination)
        {
            if (_simulation == null)
            {
                return;
            }

            for (int i = 0; i < destination.Length; i++)
            {
                Int2 position = _simulation.GetActor(i).PositionMm;
                destination[i] = new Vector3(
                    position.X / millimetersPerUnityUnit,
                    0f,
                    position.Y / millimetersPerUnityUnit);
            }
        }
    }
}

