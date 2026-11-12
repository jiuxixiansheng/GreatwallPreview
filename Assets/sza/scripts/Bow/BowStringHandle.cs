using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Sza.Bow
{
    public sealed class BowStringHandle : XRSimpleInteractable, IXRSelectFilter
    {
        public NetworkMagicBowWeapon weapon;
        public bool canProcess => isActiveAndEnabled;
        protected override void Awake()
        {
            base.Awake();
            selectFilters.Add(this);
            selectEntered.AddListener(Begin);
            selectExited.AddListener(End);
        }
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            weapon != null && weapon.isActiveAndEnabled &&
            (isSelected ? IsSelected(interactor) && weapon.IsLocalHolder : weapon.CanStartPull(interactor));
        private void Begin(SelectEnterEventArgs args) => weapon.BeginPull(args.interactorObject);
        private void End(SelectExitEventArgs args) => weapon.EndPull(!args.isCanceled);
        protected override void OnDestroy()
        {
            selectFilters.Remove(this);
            selectEntered.RemoveListener(Begin);
            selectExited.RemoveListener(End);
            base.OnDestroy();
        }
    }
}
