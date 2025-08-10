using System;
using System.Collections.Generic;
using DG.Tweening;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Random = UnityEngine.Random;

public class HelicopterFlightpath : MonoBehaviour
{
    [SerializeField] private Transform[] _waypoints;
    [SerializeField] private Transform _missionOverWaypointLeft; // The waypoint to fly to when the mission is over
    [SerializeField] private Transform _missionOverWaypointRight; // The waypoint to fly to when the mission is over
    [SerializeField] private Transform _missionGradedWaypointRight; // The waypoint to fly to when the mission is graded
    [SerializeField] private Transform _missionGradedWaypointLeft; // The waypoint to fly to when the mission is graded
    [SerializeField] private float _flightDuration = 3f; // Duration to reach the next waypoint
    [SerializeField] private float _turnDuration = 2f; // Duration to turn towards the next waypoint

    [SerializeField] private List<Transform> _waypointsFlyToLeftWhenOver;
    [SerializeField] private List<Transform> _waypointsFlyToRightWhenOver;

    private Sequence _currentFlightSequence;

    private int _currentWaypointIndex;
    private MissionTracker _missionTracker;
    private Rigidbody _rigidbody;

    private void Awake()
    {
        _missionTracker = FindFirstObjectByType<MissionTracker>();
        _rigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        FlyToRandomWaypoint();
    }

    private void OnEnable()
    {
        _missionTracker.OnMinutePassed += OnMinutePassedHandler;
        _missionTracker.OnMissionOver += OnMissionOverHandler;
        _missionTracker.OnMissionGraded += OnMissionGradedHandler;
    }

    private void OnDisable()
    {
        _missionTracker.OnMinutePassed -= OnMinutePassedHandler;
        _missionTracker.OnMissionOver -= OnMissionOverHandler;
        _missionTracker.OnMissionGraded -= OnMissionGradedHandler;
    }

    private void OnMissionGradedHandler(int _)
    {
        _currentFlightSequence?.Kill(); // Stop any ongoing flight sequence

        // left or right when mission is graded

        if (_waypointsFlyToLeftWhenOver.Contains(_waypoints[_currentWaypointIndex]))
        {
            _currentFlightSequence = FlightSequence(_missionGradedWaypointRight, flightDurationMultiplier:0.5f);
        }
        else if (_waypointsFlyToRightWhenOver.Contains(_waypoints[_currentWaypointIndex]))
        {
            _currentFlightSequence = FlightSequence(_missionGradedWaypointLeft, flightDurationMultiplier:0.5f);
        }
        else
        {
            // Default behavior if no specific waypoint is found
            _currentFlightSequence = FlightSequence(_missionGradedWaypointRight, flightDurationMultiplier:0.5f);
        }
    }

    private void OnMinutePassedHandler(int currentMinute)
    {
        if (currentMinute >= 0)
        {
            FlyToRandomWaypoint();
        }
    }

    private void OnMissionOverHandler()
    {
        _currentFlightSequence?.Kill(); // Stop any ongoing flight sequence

        // left or right when mission is over

        if (_waypointsFlyToLeftWhenOver.Contains(_waypoints[_currentWaypointIndex]))
        {
            _currentFlightSequence = FlightSequence(_missionOverWaypointLeft);
        }
        else if (_waypointsFlyToRightWhenOver.Contains(_waypoints[_currentWaypointIndex]))
        {
            _currentFlightSequence = FlightSequence(_missionOverWaypointRight);
        }
        else
        {
            // Default behavior if no specific waypoint is found
            _currentFlightSequence = FlightSequence(_missionOverWaypointLeft);
        }
    }

    private int GetNextOrPreviousWaypointIndex(int currentWaypointIndex)
    {
        if (currentWaypointIndex < 0) return 0; // Ensure index is non-negative

        // make next or previous random 
        bool next = Random.Range(0, 2) == 0; // Randomly choose next or previous

        int nextIndex = next
            ? (currentWaypointIndex + 1) % _waypoints.Length
            : (currentWaypointIndex - 1 + _waypoints.Length) % _waypoints.Length;
        return nextIndex;
    }

    private Sequence FlightSequence(Transform target, Ease ease = Ease.InOutCubic, float flightDurationMultiplier = 1f)
    {
        // determine rotation to face target
        Vector3 directionToNext = (target.position - transform.position).normalized;
        Quaternion targetRotation = Quaternion.LookRotation(directionToNext, Vector3.up);

        return DOTween.Sequence()
            .Append(_rigidbody.DORotate(targetRotation.eulerAngles, _turnDuration).SetEase(ease))
            .Join(_rigidbody.DOMove(target.position, _flightDuration * flightDurationMultiplier).SetEase(ease))
            .Insert(_flightDuration * 0.5f * flightDurationMultiplier, _rigidbody
                .DORotate(target.rotation.eulerAngles, _turnDuration)
                .SetEase(Ease.InOutCubic));
    }

    private void FlyToRandomWaypoint()
    {
        _currentFlightSequence?.Kill(); // Kill any existing sequence before starting a new one

        int nextWaypointIndex = GetNextOrPreviousWaypointIndex(_currentWaypointIndex);
        Transform nextWaypoint = _waypoints[nextWaypointIndex];

        _currentFlightSequence = FlightSequence(nextWaypoint);
        _currentWaypointIndex = nextWaypointIndex;
    }

    private void OnDestroy()
    {
        _currentFlightSequence?.Kill(); // Clean up the sequence if the object is destroyed
    }
}