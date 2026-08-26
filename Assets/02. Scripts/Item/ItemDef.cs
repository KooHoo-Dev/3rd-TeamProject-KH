using UnityEngine;

namespace Item
{
    public enum ItemType
    {
        Exchange,
        Pickaxe,
        Dynamite
    }

    public class ItemDTO
    {
        public int ID;
        public string Name;
        public string Description;
        public int Price;
        public string ItemType; // "Exchange", "Pickaxe", "Dynamite"
        public string PrefabKey; // 프리팹 리소스 경로 키
        public string SpriteKey; // 스프라이트 리소스 경로 키

        // 곡괭이 전용 데이터
        public float PickaxeRange;
        public int DigPower;

        // 다이너마이트 전용 데이터
        public float ExplosionRadius;
        public int ExplosionPower;
    }

    // 가독성과 성능을 위해 값 타입 구조체(struct)로 기능 데이터 정의
    public struct PickaxeInfo
    {
        public float Range { get; }
        public int DigPower { get; }
        public PickaxeInfo(float range, int digPower) => (Range, DigPower) = (range, digPower);
    }

    public struct DynamiteInfo
    {
        public float Radius { get; }
        public int Power { get; }
        public DynamiteInfo(float radius, int power) => (Radius, Power) = (radius, power);
    }

    public class ItemDef
    {
        public int ID { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public int Price { get; private set; }
        public ItemType Type { get; private set; }
        public Item Prefab { get; private set; } // 키 해석 완료된 실제 프리팹
        public Sprite Sprite { get; private set; }  // 키 해석 완료된 실제 스프라이트

        // 타입에 따라 안전하게 제공되는 스탯 정보
        public PickaxeInfo? PickaxeData { get; private set; }
        public DynamiteInfo? DynamiteData { get; private set; }

        public ItemDef(ItemDTO dto, Item prefab, Sprite sprite)
        {
            ID = dto.ID;
            Name = dto.Name;
            Description = dto.Description;
            Price = dto.Price;
            Type = System.Enum.Parse<ItemType>(dto.ItemType);
            Prefab = prefab; // 리소스를 직접 찾아와 매핑(Key Resolution)
            Sprite = sprite;

            // 아이템 타입에 맞게 데이터 조립
            if (Type == ItemType.Pickaxe)
            {
                PickaxeData = new PickaxeInfo(dto.PickaxeRange, dto.DigPower);
            }
            else if (Type == ItemType.Dynamite)
            {
                DynamiteData = new DynamiteInfo(dto.ExplosionRadius, dto.ExplosionPower);
            }
        }
    }

}