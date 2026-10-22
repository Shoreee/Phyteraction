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

        void OnDisable()
        {
            foreach (var hand in hands) if (hand != null) hand.ClearSimulationContacts();
        }

        public bool CanApplyReaction(Rigidbody body)
        {
            var hand = body.GetComponentInParent<PhysicsHand>();
            return hand == null || hand.isActiveAndEnabled && hand.GoodTracking;
        }
        public void NotifyPhysicalContact(Rigidbody body, Vector3 outwardNormal, Vector3 point) => body.GetComponentInParent<PhysicsHand>()?.NotifyContactConstraint(body, outwardNormal, point);
    }
}
