using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Gives the list a measured, eased glide instead of a jump per wheel notch.
    ///
    /// <c>ScrollRect.OnScroll</c> moves the content immediately by <c>scrollSensitivity</c>, with no
    /// easing — one notch is a hard teleport. Feeding its <see cref="ScrollRect.velocity"/> instead
    /// smooths it out, but then the distance travelled depends on the deceleration curve, so
    /// "move less per notch" and "glide for longer" pull against each other and neither is
    /// directly settable.
    ///
    /// So the wheel moves an explicit target instead, and the content eases toward it. Distance per
    /// notch is <see cref="StepPixels"/> exactly; how it gets there is <see cref="SmoothTime"/>
    /// alone. The two are independent, which is what makes them tunable.
    ///
    /// Dragging is left entirely to the ScrollRect: this steps aside while a drag is in progress
    /// and resyncs afterwards, so the two never fight over the content position.
    /// </summary>
    //
    // The interfaces are Mono-only. Il2CppInterop emits Unity's EventSystems handler
    // interfaces as CLASSES, so a MonoBehaviour cannot also implement them — see
    // docs/08-IL2CPP-PLAN.md. Without them this component simply never receives events:
    // _gliding stays false, LateUpdate returns immediately, and the ScrollRect handles the
    // wheel with its own default behaviour. Degraded, not broken.
#if IL2CPP
    internal sealed class SmoothScroll : MonoBehaviour
#else
    internal sealed class SmoothScroll : MonoBehaviour, IScrollHandler, IBeginDragHandler, IEndDragHandler
#endif
    {
        public ScrollRect Target;

        /// <summary>Content pixels per wheel notch. Set by the screen from its row height.</summary>
        public float StepPixels = 112f;

        /// <summary>Roughly how long the glide takes to arrive. Higher is lazier.</summary>
        public float SmoothTime = 0.14f;

        /// <summary>Raised whenever this moves the content, so rows can be recycled immediately.</summary>
        public Action Moved;

        private float _targetY;
        private float _speed;
        private bool _gliding;
        private bool _dragging;

        /// <summary>Abandons an in-flight glide — used when the list itself changes underneath it.</summary>
        public void Cancel()
        {
            _gliding = false;
            _speed = 0f;
        }

        public void OnScroll(PointerEventData eventData)
        {
            var content = Target != null ? Target.content : null;
            if (content == null || _dragging) return;

            var notches = eventData.scrollDelta.y;
            if (Mathf.Approximately(notches, 0f)) return;

            // Start from where the content actually is, not from a stale target: the list may have
            // been rebuilt, dragged, or clamped since the last notch.
            if (!_gliding)
            {
                _targetY = content.anchoredPosition.y;
                _speed = 0f;
                _gliding = true;
            }

            // Only the DIRECTION is taken from the event, never the magnitude.
            //
            // scrollDelta is not normalised: depending on the platform and mouse driver one notch
            // arrives as 1, 3, or a fraction, so multiplying by it made the real distance per notch
            // unpredictable — two attempts at tuning "rows per notch" both landed at three or four
            // because the multiplier was never 1. One event moves one step, and a fast spin simply
            // sends more events.
            var direction = notches > 0f ? 1f : -1f;

            // Rolling up walks BACK up the list, which means decreasing the content's anchored Y.
            _targetY = Mathf.Clamp(_targetY - direction * StepPixels, 0f, MaximumScroll());
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            Cancel();
        }

        public void OnEndDrag(PointerEventData eventData) => _dragging = false;

        private void LateUpdate()
        {
            if (!_gliding || _dragging) return;

            var content = Target != null ? Target.content : null;
            if (content == null) { Cancel(); return; }

            var position = content.anchoredPosition;
            var next = Mathf.SmoothDamp(position.y, _targetY, ref _speed, SmoothTime);

            // Settle exactly on the target rather than creeping toward it forever.
            if (Mathf.Abs(next - _targetY) < 0.5f)
            {
                next = _targetY;
                Cancel();
            }

            content.anchoredPosition = new Vector2(position.x, next);

            // ScrollRect notices the move in its own LateUpdate and fires onValueChanged, but the
            // order between the two components is not defined — so the rows are refreshed directly
            // rather than a frame late.
            Moved?.Invoke();
        }

        private float MaximumScroll()
        {
            var viewportHeight = Target.viewport != null ? Target.viewport.rect.height : 0f;
            return Mathf.Max(0f, Target.content.sizeDelta.y - viewportHeight);
        }
    }
}
