using System;
using System.Collections.Generic;
using PhysicsHands.Dynamics;
using PhysicsHands.Hands;
using PhysicsHands.Simulation;
using UnityEngine;

namespace PhysicsHands.App
{
    public sealed class HandContactSource : MonoBehaviour, ISimulationContactSource
    {
        public PhysicsHand[] hands = Array.Empty<PhysicsHand>();
        public void Gather(ContactGatherer contacts, float deltaTime)
        {
            foreach (var hand in hands)
            {
                if (hand == null || !hand.isActiveAndEnabled || !hand.GoodTracking)
                    continue;
                foreach (var collider in hand.BoneColliders)
                {
                    contacts.AddCollider(collider, deltaTime);
                }
            }
        }

        public bool UseSampledVelocity(Rigidbody body) => body.GetComponentInParent<PhysicsHand>()?.DirectTracking == true;
        public void NotifyPhysicalContact(Rigidbody body) => body.GetComponentInParent<PhysicsHand>()?.NotifyPhysicalContact(body);
    }
}
