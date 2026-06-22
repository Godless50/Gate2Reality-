using UnityEngine;

namespace Gate2Reality.Narrative
{
    [DisallowMultipleComponent]
    public class NarrativeEndingDirector : MonoBehaviour
    {
        public enum EndingType { Unknown, ReturnObject, LeaveRoom, AcceptPresence }

        [SerializeField] private NarrativeManager narrativeManager;
        [SerializeField] private NarrativeContextCollector contextCollector;
        [SerializeField] private float activationTriggerCount = 5f;

        private EndingType _selectedEnding = EndingType.Unknown;
        private bool _endingActive;

        public bool IsEndingActive => _endingActive;
        public EndingType SelectedEnding => _selectedEnding;

        public event System.Action<EndingType> OnEndingSelected;
        public event System.Action OnEndingCompleted;

        private void Update()
        {
            if (_endingActive) return;
            if (narrativeManager.CurrentStage == NarrativeManager.NarrativeStage.Crisis)
                SelectEnding();
        }

        private void SelectEnding()
        {
            var ctx = contextCollector.Capture(NarrativeLabel.None);
            bool hasChild = ctx.HasSeen(NarrativeLabel.TeddyBear);
            bool hasSharp = ctx.HasSeen(NarrativeLabel.Knife) || ctx.HasSeen(NarrativeLabel.Scissors);

            if (hasChild)        _selectedEnding = EndingType.ReturnObject;
            else if (hasSharp)   _selectedEnding = EndingType.LeaveRoom;
            else                 _selectedEnding = EndingType.AcceptPresence;

            _endingActive = true;
            OnEndingSelected?.Invoke(_selectedEnding);
        }

        public void CompleteEnding()
        {
            if (!_endingActive) return;
            _endingActive = false;
            OnEndingCompleted?.Invoke();
        }
    }
}
