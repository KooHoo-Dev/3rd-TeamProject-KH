using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>확정된 내구도의 균열 표시. 손상 셀만 View를 가지며 Offset은 World에서 읽는다.</summary>
    [DisallowMultipleComponent]
    public sealed class TerrainCrackOverlayManager : MonoBehaviour
    {
        // 기존 Terrain=0, Resource=2, Debug label=3, Highlight=20 사이의 표면층.
        private const int CrackSortingOrder = 4;
        private static readonly int[] DamageThresholds = { 20, 40, 60, 80 };

        [Header("Rendering")]
        // Inspector에서 직접 할당하는 균열 오버레이 전용 Lit 머터리얼.
        // 공개 필드로 두어 런타임에 추가된 컴포넌트에서도 항상 노출되게 한다.
        public Material CrackMaterial;

        private readonly struct CrackStage
        {
            public readonly int DamagePercent;
            public readonly Sprite Sprite;
            public CrackStage(int damagePercent, Sprite sprite)
            {
                DamagePercent = damagePercent;
                Sprite = sprite;
            }
        }

        private readonly Dictionary<Vector2Int, SpriteRenderer> views = new();
        private readonly Stack<SpriteRenderer> pool = new();
        private CrackStage[] stages;
        private Transform viewRoot;

        /// <summary>누적 손상률의 가장 높은 통과 단계. 0은 표시 없음.</summary>
        public static int GetDamageStage(int current, int maximum)
        {
            if (maximum <= 0 || current <= 0 || current >= maximum) return 0;
            long damage = (long)maximum - current;
            for (int i = DamageThresholds.Length - 1; i >= 0; i--)
                if (damage * 100 >= (long)maximum * DamageThresholds[i]) return DamageThresholds[i];
            return 0;
        }

        public void RebuildAll(TerrainGridWorld world)
        {
            Clear();
            EnsureStages();
            foreach (Vector2Int cell in world.GetOccupiedCells()) RefreshCell(world, cell);
        }

        public void RefreshCell(TerrainGridWorld world, Vector2Int coord)
        {
            TerrainCell cell = world.Data?.GetCellOrNull(coord);
            int stage = cell != null && world.TryGetMaxDurability(coord, out int maximum)
                ? GetDamageStage(cell.CurrentDurability, maximum) : 0;
            if (stage == 0)
            {
                RemoveView(coord);
                return;
            }

            EnsureStages();
            Sprite sprite = null;
            foreach (CrackStage candidate in stages)
                if (candidate.DamagePercent == stage) sprite = candidate.Sprite;
            if (sprite == null || sprite.bounds.size.x <= 0f || sprite.bounds.size.y <= 0f)
            {
                RemoveView(coord);
                return;
            }

            if (views.TryGetValue(coord, out SpriteRenderer view) == false || view == null)
            {
                view = RentView();
                views[coord] = view;
            }
            view.sprite = sprite;
            Vector3 size = sprite.bounds.size;
            view.transform.localScale = new Vector3(world.CellSize / size.x, world.CellSize / size.y, 1f);
            RefreshPosition(world, coord);
            view.gameObject.SetActive(true);
        }

        public void RefreshPosition(TerrainGridWorld world, Vector2Int coord)
        {
            if (views.TryGetValue(coord, out SpriteRenderer view) == false || view == null || view.sprite == null) return;
            Vector3 pivotOffset = Vector3.Scale(view.sprite.bounds.center, view.transform.localScale);
            view.transform.position = world.CellToWorldCenter(coord) + world.GetCellVisualOffset(coord) - pivotOffset;
        }

        public void Clear()
        {
            foreach (SpriteRenderer view in views.Values) ReturnView(view);
            views.Clear();
        }

        private void RemoveView(Vector2Int coord)
        {
            if (views.Remove(coord, out SpriteRenderer view)) ReturnView(view);
        }

        private void ReturnView(SpriteRenderer view)
        {
            if (view == null) return;
            view.sprite = null;
            view.gameObject.SetActive(false);
            pool.Push(view);
        }

        private SpriteRenderer RentView()
        {
            while (pool.Count > 0)
            {
                SpriteRenderer view = pool.Pop();
                if (view != null) return view;
            }
            if (viewRoot == null)
            {
                GameObject root = new("TerrainCrackOverlays");
                root.transform.SetParent(transform, false);
                viewRoot = root.transform;
                root.SetActive(isActiveAndEnabled);
            }
            GameObject overlay = new("Terrain Crack", typeof(SpriteRenderer));
            overlay.transform.SetParent(viewRoot, false);
            overlay.layer = gameObject.layer;
            SpriteRenderer renderer = overlay.GetComponent<SpriteRenderer>();
            if (CrackMaterial != null)
                renderer.sharedMaterial = CrackMaterial;
            renderer.sortingOrder = CrackSortingOrder;
            return renderer;
        }

        private void EnsureStages()
        {
            if (stages != null) return;
            stages = new CrackStage[DamageThresholds.Length];
            for (int i = 0; i < DamageThresholds.Length; i++)
            {
                int threshold = DamageThresholds[i];
                string name = $"crackOverlay{threshold}";
                Sprite[] sprites = Resources.LoadAll<Sprite>($"Tile/{name}");
                Sprite selected = null;
                foreach (Sprite sprite in sprites)
                    if (sprite.name == name || sprite.name == name + "_0") selected = sprite;
                if (selected == null && sprites.Length == 1) selected = sprites[0];
                if (selected == null)
                    Debug.LogWarning($"[Terrain Crack] Sprite subasset missing or ambiguous: Tile/{name}", this);
                stages[i] = new CrackStage(threshold, selected);
            }
        }

        private void OnEnable()
        {
            if (viewRoot != null) viewRoot.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            if (viewRoot != null) viewRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            views.Clear();
            pool.Clear();
            if (viewRoot != null) Destroy(viewRoot.gameObject);
        }
    }
}
