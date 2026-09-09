using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>TutorialButton 호버 중에만 안내 이미지와 배경 Dimmer를 표시한다.</summary>
    public sealed class TutorialHoverView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private GameObject dimmer;
        [SerializeField] private GameObject tutorialImage;

        [Header("Foreground Sorting")]
        [SerializeField] private int dimmerSortingOrder = 1000;

        private void Awake()
        {
            dimmer ??= transform.parent?.Find("Dimmer")?.gameObject;
            tutorialImage ??= transform.Find("TutorialImage")?.gameObject;

            SetRaycastTargets(tutorialImage, false);
            SetRaycastTargets(dimmer, false);
            SetForegroundSorting(dimmer, dimmerSortingOrder);
            SetForegroundSorting(tutorialImage, dimmerSortingOrder + 1);
            SetVisible(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            SetVisible(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetVisible(false);
        }

        private void SetVisible(bool isVisible)
        {
            dimmer?.SetActive(isVisible);
            tutorialImage?.SetActive(isVisible);

            foreach (LobbyPlayerSlotView slot in FindObjectsByType<LobbyPlayerSlotView>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                slot.SetTutorialOverlayVisible(isVisible);
        }

        private static void SetRaycastTargets(GameObject root, bool isEnabled)
        {
            if (root == null) return;

            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = isEnabled;
        }

        private static void SetForegroundSorting(GameObject root, int sortingOrder)
        {
            if (root == null) return;

            Canvas canvas = root.GetComponent<Canvas>();
            if (canvas == null) canvas = root.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
        }
    }
}
