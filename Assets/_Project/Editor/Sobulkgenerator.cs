#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using DeadZone.Core;

namespace DeadZone.Editor
{
    public static class SOBulkGenerator
    {
        
        [MenuItem("DeadZone/Data/Generate All SO Assets", priority = 100)]
        public static void GenerateAll()
        {
            int created = 0;
            created += GenerateWeapons();
            created += GenerateAmmo();
            created += GenerateArmor();
            created += GenerateHelmets();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "DEADZONE SO Generator",
                $"완료! 총 {created}개 자산 생성됨.\n\n" +
                "Assets/_Project/Data/ 하위를 확인하세요.\n" +
                "(이미 존재하는 자산은 스킵됨)",
                "OK");
        }

        // ═══════════ 개별 메뉴 ═══════════

        [MenuItem("DeadZone/Data/Generate Weapons Only")]
        public static void GenWeaponsMenu()
        {
            int n = GenerateWeapons();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SOGen] WeaponDataSO {n}개 생성 완료");
        }

        [MenuItem("DeadZone/Data/Generate Ammo Only")]
        public static void GenAmmoMenu()
        {
            int n = GenerateAmmo();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SOGen] AmmoDataSO {n}개 생성 완료");
        }

        [MenuItem("DeadZone/Data/Generate Armor Only")]
        public static void GenArmorMenu()
        {
            int n = GenerateArmor();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SOGen] ArmorDataSO {n}개 생성 완료");
        }

        [MenuItem("DeadZone/Data/Generate Helmets Only")]
        public static void GenHelmetsMenu()
        {
            int n = GenerateHelmets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SOGen] HelmetDataSO {n}개 생성 완료");
        }
        
        static int GenerateWeapons()
        {
            string folder = "Assets/_Project/Data/Weapons";
            EnsureFolder(folder);
            int count = 0;

            //                    file                id                  name           cat                      dmg   rate  mag  rngMin rngMax ammo               modes                              vel    flash  ads   spMin spMax spRec  dur   rarity price
            var list = new (string file, string id, string name, WeaponCategory cat,
                float dmg, float rate, int mag, float rngMin, float rngMax,
                AmmoType ammo, FireMode[] modes, float vel, float flash, float ads,
                float spMin, float spMax, float spRec, float dur,
                int rarity, int price)[]
            {
                // ── 일반 (Common = 0) ──
                ("Weapon_Glock17",       "Weapon_Glock17",       "글락-17",       WeaponCategory.Handgun,  20f,   8f,  17,   0f,   30f, AmmoType.Handgun, new[]{FireMode.Semi},                360f, 0.2f, 0.15f, 1.0f, 4.0f, 12f, 100f, 0,  1500),
                ("Weapon_SelfDefense",   "Weapon_SelfDefense",   "호신용 권총",   WeaponCategory.Handgun,  14f,   6f,   8,   0f,   20f, AmmoType.Handgun, new[]{FireMode.Semi},                300f, 0.2f, 0.15f, 1.2f, 5.0f, 10f, 100f, 0,   800),

                // ── 희귀 (Uncommon = 1) ──
                ("Weapon_Revolver",      "Weapon_Revolver",      "리볼버",        WeaponCategory.Handgun,  48f,   3f,   6,   0f,   35f, AmmoType.Handgun, new[]{FireMode.Semi},                450f, 0.2f, 0.20f, 1.0f, 5.0f, 10f, 100f, 1,  5000),
                ("Weapon_PumpSG",        "Weapon_PumpSG",        "펌프 샷건",     WeaponCategory.Shotgun,  85f,  1.2f,  7,   0f,   25f, AmmoType.Shotgun, new[]{FireMode.Pump},                400f, 0.2f, 0.25f, 2.0f, 8.0f,  8f, 100f, 1,  4000),

                // ── 레어 (Rare = 2) ──
                ("Weapon_SK74",          "Weapon_SK74",          "SK-74",         WeaponCategory.AR,       38f,  10f,  30,  30f,  180f, AmmoType.AR,      new[]{FireMode.Full,FireMode.Semi},  900f, 0.2f, 0.20f, 0.6f, 4.0f,  9f, 100f, 2, 12000),
                ("Weapon_B90",           "Weapon_B90",           "B90",           WeaponCategory.Shotgun,  72f,  3.5f,  7,   0f,   30f, AmmoType.Shotgun, new[]{FireMode.Semi},                400f, 0.2f, 0.25f, 2.2f, 8.5f,  7f, 100f, 2, 18000),
                ("Weapon_MB7",           "Weapon_MB7",           "MB-7",          WeaponCategory.SMG,      26f,  14f,  35,   0f,   75f, AmmoType.SMG,     new[]{FireMode.Full,FireMode.Semi},  680f, 0.2f, 0.18f, 0.6f, 3.5f, 11f, 100f, 2, 15000),

                // ── 에픽 (Epic = 3) ──
                ("Weapon_F2",            "Weapon_F2",            "F2",            WeaponCategory.AR,       42f,  14f,  25,  30f,  250f, AmmoType.AR,      new[]{FireMode.Full,FireMode.Semi},  950f, 0.2f, 0.20f, 0.4f, 2.8f, 11f, 100f, 3, 45000),
                ("Weapon_BoltSR",        "Weapon_BoltSR",        "드라그 소총",   WeaponCategory.Sniper,   95f,  1.5f,  5, 100f,  600f, AmmoType.Sniper,  new[]{FireMode.Bolt},                830f, 0.3f, 0.30f, 0.1f, 1.0f, 15f, 100f, 3, 35000),
            };

            foreach (var w in list)
            {
                string path = $"{folder}/{w.file}.asset";
                if (AssetDatabase.LoadAssetAtPath<WeaponDataSO>(path) != null)
                {
                    Debug.Log($"[SOGen] SKIP (이미 존재): {path}");
                    continue;
                }

                var so = ScriptableObject.CreateInstance<WeaponDataSO>();

                so.weaponCategory    = w.cat;
                so.damage            = w.dmg;
                so.fireRate          = w.rate;
                so.magSize           = w.mag;
                so.engageRange       = new Vector2(w.rngMin, w.rngMax);
                so.ammoType          = w.ammo;
                so.availableModes    = w.modes;
                so.muzzleVelocity    = w.vel;
                so.muzzleFlashOffset = w.flash;
                so.adsTransitionTime = w.ads;
                so.spreadAngle       = new Vector2(w.spMin, w.spMax);
                so.spreadRecovery    = w.spRec;
                so.maxDurability     = w.dur;

                SetBaseFields(so, w.id, w.name,
                    ItemCategory.Weapon, (RarityTier)w.rarity,
                    Vector2Int.one, 1, w.price);

                AssetDatabase.CreateAsset(so, path);
                count++;
            }

            return count;
        }
        
        static int GenerateAmmo()
        {
            string folder = "Assets/_Project/Data/Ammo";
            EnsureFolder(folder);
            int count = 0;

            var list = new (string file, string id, string name,
                AmmoType cal, AmmoGrade grade, int pen,
                float dmgMul, float priceMul, float velMul, float drag,
                int rarity, int stack, int price)[]
            {
                // AR — LP=2, BP=4, AP=6
                ("Ammo_AR_LP",       "Ammo_AR_LP",      "AR LP 탄약",       AmmoType.AR,      AmmoGrade.LP,  2, 1.2f, 1.0f, 0.9f, 0.002f, 0, 60,   300),
                ("Ammo_AR_BP",       "Ammo_AR_BP",      "AR BP 탄약",       AmmoType.AR,      AmmoGrade.BP,  4, 1.0f, 2.0f, 1.0f, 0.002f, 1, 60,   600),
                ("Ammo_AR_AP",       "Ammo_AR_AP",      "AR AP 탄약",       AmmoType.AR,      AmmoGrade.AP,  6, 0.8f, 4.0f, 1.1f, 0.002f, 2, 60,  1200),
                // SMG — LP=2, BP=3, AP=5
                ("Ammo_SMG_LP",      "Ammo_SMG_LP",     "SMG LP 탄약",      AmmoType.SMG,     AmmoGrade.LP,  2, 1.2f, 1.0f, 0.9f, 0.002f, 0, 60,   300),
                ("Ammo_SMG_BP",      "Ammo_SMG_BP",     "SMG BP 탄약",      AmmoType.SMG,     AmmoGrade.BP,  3, 1.0f, 2.0f, 1.0f, 0.002f, 1, 60,   600),
                ("Ammo_SMG_AP",      "Ammo_SMG_AP",     "SMG AP 탄약",      AmmoType.SMG,     AmmoGrade.AP,  5, 0.8f, 4.0f, 1.1f, 0.002f, 2, 60,  1200),
                // Handgun — LP=2, BP=3, AP=4
                ("Ammo_Handgun_LP",  "Ammo_Handgun_LP", "권총 LP 탄약",     AmmoType.Handgun, AmmoGrade.LP,  2, 1.2f, 1.0f, 0.9f, 0.002f, 0, 60,   300),
                ("Ammo_Handgun_BP",  "Ammo_Handgun_BP", "권총 BP 탄약",     AmmoType.Handgun, AmmoGrade.BP,  3, 1.0f, 2.0f, 1.0f, 0.002f, 1, 60,   600),
                ("Ammo_Handgun_AP",  "Ammo_Handgun_AP", "권총 AP 탄약",     AmmoType.Handgun, AmmoGrade.AP,  4, 0.8f, 4.0f, 1.1f, 0.002f, 2, 60,  1200),
                // Sniper (7.62mm) — LP=4, BP=5, AP=6
                ("Ammo_Sniper_LP",   "Ammo_Sniper_LP",  "7.62mm LP 탄약",   AmmoType.Sniper,  AmmoGrade.LP,  4, 1.2f, 1.0f, 0.9f, 0.001f, 0, 30,   300),
                ("Ammo_Sniper_BP",   "Ammo_Sniper_BP",  "7.62mm BP 탄약",   AmmoType.Sniper,  AmmoGrade.BP,  5, 1.0f, 2.0f, 1.0f, 0.001f, 1, 30,   600),
                ("Ammo_Sniper_AP",   "Ammo_Sniper_AP",  "7.62mm AP 탄약",   AmmoType.Sniper,  AmmoGrade.AP,  6, 0.8f, 4.0f, 1.1f, 0.001f, 2, 30,  1200),
                // Shotgun (12ga) — LP=1, BP=2, AP=4
                ("Ammo_SG_LP",       "Ammo_SG_LP",      "12ga 벅샷",        AmmoType.Shotgun, AmmoGrade.LP,  1, 1.2f, 1.0f, 0.9f, 0.005f, 0, 30,   300),
                ("Ammo_SG_BP",       "Ammo_SG_BP",      "12ga 슬러그",      AmmoType.Shotgun, AmmoGrade.BP,  2, 1.0f, 2.0f, 1.0f, 0.005f, 1, 30,   600),
                ("Ammo_SG_AP",       "Ammo_SG_AP",      "12ga AP 슬러그",   AmmoType.Shotgun, AmmoGrade.AP,  4, 0.8f, 4.0f, 1.1f, 0.005f, 2, 30,  1200),
            };

            foreach (var a in list)
            {
                string path = $"{folder}/{a.file}.asset";
                if (AssetDatabase.LoadAssetAtPath<AmmoDataSO>(path) != null)
                {
                    Debug.Log($"[SOGen] SKIP (이미 존재): {path}");
                    continue;
                }

                var so = ScriptableObject.CreateInstance<AmmoDataSO>();

                so.caliber            = a.cal;
                so.grade              = a.grade;
                so.penetration        = a.pen;
                so.damageMultiplier   = a.dmgMul;
                so.priceMultiplier    = a.priceMul;
                so.velocityMultiplier = a.velMul;
                so.dragCoefficient    = a.drag;

                SetBaseFields(so, a.id, a.name,
                    ItemCategory.Ammo, (RarityTier)a.rarity,
                    Vector2Int.one, a.stack, a.price);

                AssetDatabase.CreateAsset(so, path);
                count++;
            }

            return count;
        }
        
        static int GenerateArmor()
        {
            string folder = "Assets/_Project/Data/Armor";
            EnsureFolder(folder);
            int count = 0;

            var list = new (string file, string id, string name,
                ArmorClass cls, float dur, float spd, float block,
                int rarity, int price)[]
            {
                //            file          id           name        class          dur    spd     block  rarity price
                ("Armor_C1", "Armor_C1", "C1 아머", ArmorClass.C1,   50f,  0f,     0.10f, 0,  1000),
                ("Armor_C2", "Armor_C2", "C2 아머", ArmorClass.C2,   80f, -0.02f,  0.15f, 0,  3000),
                ("Armor_C3", "Armor_C3", "C3 아머", ArmorClass.C3,  120f, -0.05f,  0.20f, 1,  8000),
                ("Armor_C4", "Armor_C4", "C4 아머", ArmorClass.C4,  180f, -0.08f,  0.25f, 1, 15000),
                ("Armor_C5", "Armor_C5", "C5 아머", ArmorClass.C5,  250f, -0.14f,  0.30f, 2, 35000),
                ("Armor_C6", "Armor_C6", "C6 아머", ArmorClass.C6,  350f, -0.20f,  0.35f, 3, 60000),
            };

            foreach (var a in list)
            {
                string path = $"{folder}/{a.file}.asset";
                if (AssetDatabase.LoadAssetAtPath<ArmorDataSO>(path) != null)
                {
                    Debug.Log($"[SOGen] SKIP (이미 존재): {path}");
                    continue;
                }

                var so = ScriptableObject.CreateInstance<ArmorDataSO>();

                so.armorClass       = a.cls;
                so.maxDurability    = a.dur;
                so.moveSpeedPenalty = a.spd;
                so.blockChance      = a.block;

                SetBaseFields(so, a.id, a.name,
                    ItemCategory.Armor, (RarityTier)a.rarity,
                    Vector2Int.one, 1, a.price);

                AssetDatabase.CreateAsset(so, path);
                count++;
            }

            return count;
        }

        static int GenerateHelmets()
        {
            string folder = "Assets/_Project/Data/Helmets";
            EnsureFolder(folder);
            int count = 0;

            var list = new (string file, string id, string name,
                int helmetClass, float dur, float spd, float block,
                int rarity, int price)[]
            {
                //              file           id            name          class dur   spd    block  rarity price
                ("Helmet_C1", "Helmet_C1", "C1 헬멧",       1,   30f,  0f,     0.25f, 0,   800),
                ("Helmet_C2", "Helmet_C2", "C2 헬멧",       2,   55f, -0.01f,  0.35f, 0,  2500),
                ("Helmet_C3", "Helmet_C3", "C3 헬멧",       3,   85f, -0.03f,  0.50f, 1,  7000),
                ("Helmet_C4", "Helmet_C4", "C4 헬멧",       4,  130f, -0.06f,  0.65f, 2, 20000),
            };

            foreach (var h in list)
            {
                string path = $"{folder}/{h.file}.asset";
                if (AssetDatabase.LoadAssetAtPath<HelmetDataSO>(path) != null)
                {
                    Debug.Log($"[SOGen] SKIP (이미 존재): {path}");
                    continue;
                }

                var so = ScriptableObject.CreateInstance<HelmetDataSO>();

                so.helmetClass      = (HelmetClass)h.helmetClass;
                so.maxDurability    = h.dur;
                so.moveSpeedPenalty = h.spd;
                so.blockChance      = h.block;

                SetBaseFields(so, h.id, h.name,
                    ItemCategory.Armor, (RarityTier)h.rarity,
                    Vector2Int.one, 1, h.price);

                AssetDatabase.CreateAsset(so, path);
                count++;
            }

            return count;
        }

        // ══════════════════════════════════════════════════════
        //  공통 베이스 필드 세팅
        // ══════════════════════════════════════════════════════

        static void SetBaseFields(
            ItemDataSO so,
            string itemID,
            string displayName,
            ItemCategory category,
            RarityTier rarity,
            Vector2Int gridSize,
            int maxStackSize,
            int baseSellPrice)
        {
            so.itemID        = itemID;
            so.displayName   = displayName;
            so.category      = category;
            so.rarity        = rarity;
            so.gridSize      = gridSize;
            so.maxStackSize  = maxStackSize;
            so.baseSellPrice = baseSellPrice;
        }

        // ══════════════════════════════════════════════════════
        //  유틸리티
        // ══════════════════════════════════════════════════════

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string[] parts = path.Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif