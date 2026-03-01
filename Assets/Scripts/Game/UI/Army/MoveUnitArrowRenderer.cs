using System.Collections.Generic;
using Extensions;
using UnityEngine;

namespace Game.UI.Army
{
    public sealed class MoveUnitArrowRenderer
    {
        private readonly Transform _parent;
        private readonly GameObject _arrowPrefab;
        private readonly List<GameObject> _arrows = new();

        public MoveUnitArrowRenderer(Transform parent, GameObject arrowPrefab)
        {
            _parent = parent;
            _arrowPrefab = arrowPrefab;
        }

        public void Draw(Vector3 from, Vector3 to)
        {
            var direction = to - from;
            if (direction.sqrMagnitude < 0.001f) return;

            var arrow = Object.Instantiate(_arrowPrefab, from, Quaternion.LookRotation(direction), _parent);
            var distance = direction.magnitude;

            arrow.transform.position = arrow.transform.position.SetZ(-1);

            var spriteRenderer = arrow.transform.GetChild(0).GetComponent<SpriteRenderer>();
            spriteRenderer.size = new Vector2(5.78f * distance, spriteRenderer.size.y);
            spriteRenderer.transform.localPosition = spriteRenderer.transform.localPosition.SetZ(0.5f * distance);

            _arrows.Add(arrow);
        }

        public void Clear()
        {
            foreach (var arrow in _arrows)
                Object.Destroy(arrow);

            _arrows.Clear();
        }
    }
}
