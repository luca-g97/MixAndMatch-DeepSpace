using System;
using KBCore.Refs;
using UnityEngine;

public class RigidbodyTargetDirection : ValidatedMonoBehaviour
{
    [SerializeField, Self] Rigidbody rigidbody;
    [SerializeField] private int _velocity = 100;
    [SerializeField] private Transform _target;
    private float _angle;

    private void FixedUpdate()
    {
        Vector3 localTarget = transform.InverseTransformPoint(_target.position);
    
        _angle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;

        Vector3 eulerAngleVelocity =  new Vector3 (0, _angle, 0);
        Quaternion deltaRotation = Quaternion.Euler(eulerAngleVelocity * Time.fixedDeltaTime * _velocity);

        rigidbody.MoveRotation(rigidbody.rotation * deltaRotation);
    }
}
