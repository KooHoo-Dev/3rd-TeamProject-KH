using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Item
{
    public class ItemTable
    {
        private const string TableName = "Table/Item/Items.tsv";

        private Dictionary<int, ItemDef> defs = new();

        public ItemDef Get(int id) => defs[id];

        public IReadOnlyCollection<ItemDef> GetDefs() => defs.Values;
        
        public int Count => defs.Count;
        
        public void Load()
        {
            defs.Clear();
            
            // 특정 경로에 있는 파일을 읽어온다    
            string path = Application.streamingAssetsPath + "/" + TableName;
            List<ItemDTO> rows = new List<ItemDTO>();
            using (StreamReader reader = new StreamReader(path))
            {
                // 첫째줄(헤더)의 타입정의는 건너뛴다 (아래 내용)
                // ID	PrefabKey	SpriteKey	MaxHp	Speed	Armor	Resist	KillReward	LeakDamage
                reader.ReadLine();
                while (reader.EndOfStream == false)
                {
                    ItemDTO itemDto = new();
                    string line = reader.ReadLine(); // 한줄을 통으로 읽고
                    // line : 0	Mini Defence/Prefabs/Enemy/Enemy_00	Mini Defence/Prefabs/Enemy/Enemy_00_Thumbnail	100	2	0	0	10	1
                    string[] cells = line.Split('\t'); //.Split 함수를 이용해서 탭으로 잘라준다
                    //cells[0] : 0
                    //cells[1] : Mini Defence/Prefabs/Enemy/Enemy_00
                    //cells[2] : Mini Defence/Prefabs/Enemy/Enemy_00_Thumbnail
                    //cells[3] : 100
                    //cells[4] : 2
                    //cells[5] : 0
                    //cells[6] : 0
                    //cells[7] : 10
                    //cells[8] : 1
                    
                    // 차례대로 매핑한다
                    itemDto.ID = int.Parse(cells[0]);
                    itemDto.Name = cells[1];
                    itemDto.Description = cells[2];
                    itemDto.Price = int.Parse(cells[3]);
                    itemDto.ItemType = cells[4];
                    itemDto.PrefabKey = cells[5];
                    itemDto.SpriteKey = cells[6];
                    itemDto.PickaxeRange = float.Parse(cells[7]);
                    itemDto.DigPower  = int.Parse(cells[8]);
                    itemDto.ExplosionRadius = float.Parse(cells[9]);
                    itemDto.ExplosionPower = int.Parse(cells[10]);
                    
                    rows.Add(itemDto);
                }
            }

            // List<ItemDTO> rows 를 이용해서 def 객체를 생성한다
            foreach (ItemDTO itemDto in rows)
            {
                Item prefab = Resources.Load<Item>(itemDto.PrefabKey);
                Sprite sprite = Resources.Load<Sprite>(itemDto.SpriteKey);

                defs[itemDto.ID] = new ItemDef(itemDto, prefab, sprite);
            }

            Debug.Log(defs.Count);
        }

    }
}