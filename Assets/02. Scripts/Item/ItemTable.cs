using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TerrainCollapse
{
    public class ItemTable
    {
        private const string TableName = "Table/Item/Items.tsv";

        private Dictionary<int, ItemDef> defs = new();
        private int goldItemID = -1;

        public ItemDef Get(int id) => defs[id];

        public bool TryGet(int id, out ItemDef itemDef) => defs.TryGetValue(id, out itemDef);

        public IReadOnlyCollection<ItemDef> GetDefs() => defs.Values;
        
        public int Count => defs.Count;

        public bool TryGetGold(out ItemDef itemDef)
        {
            itemDef = null;

            return goldItemID >= 0 && defs.TryGetValue(goldItemID, out itemDef);
        }

        public void Load()
        {
            defs.Clear();
            goldItemID = -1;
            
            string path = Application.streamingAssetsPath + "/" + TableName;
            List<ItemDTO> rows = new List<ItemDTO>();
            using (StreamReader reader = new StreamReader(path))
            {
                reader.ReadLine();
                while (reader.EndOfStream == false)
                {
                    ItemDTO itemDto = new();
                    string line = reader.ReadLine();
                    string[] cells = line.Split('\t'); 
                    
                    itemDto.ID = int.Parse(cells[0]);
                    itemDto.Name = cells[1];
                    itemDto.Description = cells[2];
                    itemDto.Price = int.Parse(cells[3]);
                    itemDto.ItemType = cells[4];
                    itemDto.PrefabKey = cells[5];
                    itemDto.SpriteKey = cells[6];
                    itemDto.PickaxeRange = float.Parse(cells[7]);
                    itemDto.DigPower  = int.Parse(cells[8]);
                    itemDto.ExplosionRadius = int.Parse(cells[9]);
                    itemDto.ExplosionPower = int.Parse(cells[10]);
                    
                    rows.Add(itemDto);
                }
            }

            foreach (ItemDTO itemDto in rows)
            {
                Item prefab = Resources.Load<Item>(itemDto.PrefabKey);
                Sprite sprite = Resources.Load<Sprite>(itemDto.SpriteKey);

                ItemDef itemDef = new ItemDef(itemDto, prefab, sprite);
                defs[itemDto.ID] = itemDef;

                if (itemDef.Type != ItemType.Gold) continue;
                if (goldItemID >= 0) continue;

                goldItemID = itemDef.ID;
            }

            Debug.Log(defs.Count);
        }

    }
}
