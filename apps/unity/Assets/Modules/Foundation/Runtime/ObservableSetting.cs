using System;
using UnityEngine;

namespace PhysicsHands
{
    /// <summary>
    /// Serializable value with change notification.
    /// </summary>
    [Serializable]
    public class ObservableSetting<T>
    {
        [SerializeField]
        private T _value;
        public event Action<T> Changed;
        public ObservableSetting()
        {
        }

        public ObservableSetting(T value) => _value = value;
        public T Value
        {
            get => _value;
            set
            {
                if (Equals(_value, value))
                    return;
                _value = value;
                Changed?.Invoke(value);
            }
        }

        public static implicit operator T(ObservableSetting<T> setting) => setting._value;
    }
}
