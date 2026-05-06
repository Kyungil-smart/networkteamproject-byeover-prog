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
            created += GenerateBackpacks();
            created += GenerateEnemies();
            created += GenerateQuests();

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

        [MenuItem("DeadZone/Data/Generate Backpacks Only")]
        public static void GenBackpacksMenu()
        {
            int n = GenerateBackpacks();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SOGen] BackpackDataSO {n}개 생성 완료");
        }

        [MenuItem("DeadZone/Data/Generate Enemies Only")]
        public static void GenEnemiesMenu()
        {
            int n = GenerateEnemies();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SOGen] EnemyStatsSO {n}개 생성 완료");
        }

        [MenuItem("DeadZone/Data/Generate Quests Only")]
        public static void GenQuestsMenu()
        {
            int n = GenerateQuests();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SOGen] QuestDataSO {n}개 생성 완료");
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

        static int GenerateBackpacks()
        {
            string folder = "Assets/_Project/Data/Backpacks";
            EnsureFolder(folder);
            int count = 0;

            var list = new (string file, string id, string name,
                int level, int slots, float weight,
                int rarity, int price)[]
            {
                //                file              id                name         lv  slots weight rarity  price
                ("Backpack_Lv1", "Backpack_Lv1", "Lv1 가방",         1,   5,  10f,   0,  2000),
                ("Backpack_Lv2", "Backpack_Lv2", "Lv2 가방",         2,  10,  15f,   1,  8000),
                ("Backpack_Lv3", "Backpack_Lv3", "Lv3 가방",         3,  15,  20f,   2, 20000),
                ("Backpack_Lv4", "Backpack_Lv4", "Lv4 가방",         4,  20,  25f,   3, 50000),
            };

            foreach (var b in list)
            {
                string path = $"{folder}/{b.file}.asset";
                if (AssetDatabase.LoadAssetAtPath<BackpackDataSO>(path) != null)
                {
                    Debug.Log($"[SOGen] SKIP (이미 존재): {path}");
                    continue;
                }

                var so = ScriptableObject.CreateInstance<BackpackDataSO>();

                so.backpackLevel      = b.level;
                so.extraSlots         = b.slots;
                so.extraWeightCapacity = b.weight;

                SetBaseFields(so, b.id, b.name,
                    ItemCategory.Armor, (RarityTier)b.rarity,
                    Vector2Int.one, 1, b.price);

                AssetDatabase.CreateAsset(so, path);
                count++;
            }

            return count;
        }
        
        static int GenerateEnemies()
        {
            string folder = "Assets/_Project/Data/Enemies";
            EnsureFolder(folder);
            int count = 0;

            // ── 일반 적 T1~T5 ──
            count += CreateEnemy(folder, "Enemy_T1", "T1 졸병",
                EnemyTier.T1, Faction.Scavenger, false,
                80f, 3.5f, "Armor_C1", "Weapon_Glock17", "Ammo_Handgun_LP",
                0, 0.50f,
                1.2f, 2, 1.0f,
                2.0f, 6.0f, 2.0f, 25f,
                30f, 110f, 15f, 1.5f,
                5f, 20f,
                false, false, 5f,
                "Enemy_Zone1_Any", 0);

            count += CreateEnemy(folder, "Enemy_T2", "T2 정규",
                EnemyTier.T2, Faction.Conscript, false,
                100f, 4.0f, "Armor_C2", "Weapon_Revolver", "Ammo_Handgun_BP",
                0, 0.50f,
                1.0f, 3, 0.8f,
                1.5f, 4.5f, 1.8f, 35f,
                50f, 110f, 20f, 1.0f,
                8f, 30f,
                false, false, 5f,
                "Enemy_Zone1_Any", 1);

            count += CreateEnemy(folder, "Enemy_T3", "T3 베테랑",
                EnemyTier.T3, Faction.Conscript, false,
                140f, 4.5f, "Armor_C3", "Weapon_SK74", "Ammo_AR_BP",
                0, 0.75f,
                0.8f, 3, 0.6f,
                0.8f, 3.0f, 1.5f, 60f,
                70f, 110f, 25f, 0.7f,
                15f, 50f,
                true, true, 25f,
                "", 1);

            count += CreateEnemy(folder, "Enemy_T4", "T4 엘리트",
                EnemyTier.T4, Faction.Cerberus, false,
                200f, 5.0f, "Armor_C4", "Weapon_F2", "Ammo_AR_AP",
                0, 0.75f,
                0.6f, 4, 0.5f,
                0.5f, 2.0f, 1.3f, 80f,
                90f, 110f, 30f, 0.5f,
                20f, 70f,
                true, true, 20f,
                "", 2);

            count += CreateEnemy(folder, "Enemy_T5", "T5 보스급",
                EnemyTier.T5, Faction.Cerberus, false,
                400f, 6.0f, "Armor_C6", "Weapon_F2", "Ammo_AR_AP",
                0, 1.0f,
                0.5f, 5, 0.4f,
                0.3f, 1.0f, 1.2f, 100f,
                120f, 110f, 40f, 0.3f,
                30f, 90f,
                true, true, 15f,
                "", 2);

            // ── Stage 1 보스 5종 ──

            // 창고 보스 (Q3 Kill target)
            count += CreateEnemy(folder, "Boss_S1_01", "창고 보스",
                EnemyTier.T5, Faction.Conscript, true,
                500f, 5.0f, "Armor_C4", "Weapon_SK74", "Ammo_AR_BP",
                -1, 1.0f,
                0.5f, 5, 0.4f,
                0.3f, 1.0f, 1.2f, 80f,
                100f, 110f, 35f, 0.3f,
                25f, 70f,
                true, true, 15f,
                "Boss_Warehouse", 3);

            // 군사 막사 보스 (Q4 Kill target, grenadeCooldown=10)
            count += CreateEnemy(folder, "Boss_S1_02", "군사 막사 보스",
                EnemyTier.T5, Faction.Conscript, true,
                400f, 5.0f, "Armor_C4", "Weapon_SK74", "Ammo_AR_BP",
                -1, 1.1f,
                0.5f, 5, 0.4f,
                0.3f, 1.0f, 1.2f, 80f,
                100f, 110f, 35f, 0.3f,
                25f, 70f,
                true, true, 10f,
                "Boss_MilitaryBase", 3);

            // 제재소 보스 (Q3-1 Kill target)
            count += CreateEnemy(folder, "Boss_S1_03", "제재소 보스",
                EnemyTier.T5, Faction.Cerberus, true,
                600f, 5.0f, "Armor_C5", "Weapon_F2", "Ammo_AR_AP",
                -1, 1.1f,
                0.5f, 5, 0.4f,
                0.3f, 1.0f, 1.2f, 90f,
                110f, 110f, 35f, 0.3f,
                20f, 60f,
                true, true, 15f,
                "Boss_Sawmill", 3);

            // 숲 보스 (Q2-1 Kill target, 스나이퍼)
            count += CreateEnemy(folder, "Boss_S1_04", "숲 보스",
                EnemyTier.T5, Faction.Conscript, true,
                300f, 4.5f, null, "Weapon_BoltSR", "Ammo_Sniper_AP",
                0, 1.2f,
                2.0f, 1, 2.5f,
                0.1f, 0.5f, 1.2f, 200f,
                150f, 110f, 30f, 0.3f,
                80f, 180f,
                false, false, 5f,
                "Boss_Forest_Sniper", 2);

            // 발전시설 보스 (Q2 Kill target)
            count += CreateEnemy(folder, "Boss_S1_05", "발전시설 보스",
                EnemyTier.T5, Faction.Conscript, true,
                300f, 4.5f, "Armor_C3", "Weapon_MB7", "Ammo_SMG_BP",
                -1, 1.0f,
                0.5f, 5, 0.4f,
                0.4f, 1.5f, 1.3f, 70f,
                100f, 110f, 30f, 0.3f,
                15f, 60f,
                true, true, 20f,
                "Boss_PowerPlant", 2);

            // ── Stage 2 보스 3종 (Q6 Kill target — 3종 공유 ID) ──

            // 보스 2-1
            count += CreateEnemy(folder, "Boss_S2_01", "보스 2-1",
                EnemyTier.T5, Faction.Cerberus, true,
                600f, 5.5f, "Armor_C5", "Weapon_F2", "Ammo_AR_AP",
                -1, 1.0f,
                0.5f, 5, 0.4f,
                0.3f, 1.0f, 1.2f, 100f,
                120f, 110f, 40f, 0.3f,
                25f, 80f,
                true, true, 15f,
                "Boss_Stage2_All", 3);

            // 보스 2-2 (HP 1000, C6)
            count += CreateEnemy(folder, "Boss_S2_02", "보스 2-2",
                EnemyTier.T5, Faction.Cerberus, true,
                1000f, 6.0f, "Armor_C6", "Weapon_F2", "Ammo_AR_AP",
                0, 1.2f,
                0.5f, 5, 0.4f,
                0.3f, 1.0f, 1.2f, 100f,
                120f, 110f, 40f, 0.3f,
                30f, 90f,
                true, true, 12f,
                "Boss_Stage2_All", 4);

            // 보스 2-3
            count += CreateEnemy(folder, "Boss_S2_03", "보스 2-3",
                EnemyTier.T5, Faction.Cerberus, true,
                800f, 5.5f, "Armor_C5", "Weapon_F2", "Ammo_AR_AP",
                -1, 1.1f,
                0.5f, 5, 0.4f,
                0.3f, 1.0f, 1.2f, 100f,
                120f, 110f, 40f, 0.3f,
                25f, 80f,
                true, true, 15f,
                "Boss_Stage2_All", 3);

            return count;
        }

        /// <summary>적 SO 1개 생성 헬퍼. 이미 존재하면 스킵.</summary>
        static int CreateEnemy(string folder, string file, string displayName,
            EnemyTier tier, Faction faction, bool isBoss,
            float hp, float speed,
            string armorFile, string weaponFile, string ammoFile,
            int penMod, float dmgMult,
            float fireInterval, int burstSize, float burstRest,
            float spMin, float spMax, float rangeMult, float maxRange,
            float vision, float fov, float hearing, float reaction,
            float prefMin, float prefMax,
            bool canReinforce, bool canGrenade, float grenadeCd,
            string enemyId = "", int extraLootCount = 1)
        {
            string path = $"{folder}/{file}.asset";
            if (AssetDatabase.LoadAssetAtPath<EnemyStatsSO>(path) != null)
            {
                Debug.Log($"[SOGen] SKIP (이미 존재): {path}");
                return 0;
            }

            var so = ScriptableObject.CreateInstance<EnemyStatsSO>();

            // 식별
            so.displayName = displayName;
            so.tier        = tier;
            so.faction     = faction;
            so.isBoss      = isBoss;
            so.enemyId     = enemyId;

            // 체력 & 방어
            so.maxHP        = hp;
            so.defaultArmor = armorFile != null
                ? AssetDatabase.LoadAssetAtPath<ArmorDataSO>($"Assets/_Project/Data/Armor/{armorFile}.asset")
                : null;

            // 무기 & 탄약
            so.defaultWeapon = AssetDatabase.LoadAssetAtPath<WeaponDataSO>(
                $"Assets/_Project/Data/Weapons/{weaponFile}.asset");
            so.defaultAmmo = AssetDatabase.LoadAssetAtPath<AmmoDataSO>(
                $"Assets/_Project/Data/Ammo/{ammoFile}.asset");
            so.penetrationModifier = penMod;
            so.damageMultiplier    = dmgMult;

            // 사격 타이밍
            so.fireInterval   = fireInterval;
            so.burstSize      = burstSize;
            so.burstRestDelay = burstRest;

            // 탄퍼짐
            so.spreadAngleMin        = spMin;
            so.spreadAngleMax        = spMax;
            so.rangeSpreadMultiplier = rangeMult;
            so.maxEffectiveRange     = maxRange;

            // 감지
            so.visionRange  = vision;
            so.fov          = fov;
            so.hearingRange = hearing;
            so.reactionTime = reaction;

            // 이동
            so.moveSpeed = speed;

            // 교전 거리
            so.preferredRangeMin = prefMin;
            so.preferredRangeMax = prefMax;

            // 확장 능력
            so.canCallReinforcements = canReinforce;
            so.canThrowGrenades      = canGrenade;
            so.grenadeCooldown       = grenadeCd;

            // 사망 드랍
            so.dropEquippedGear = true;
            so.extraLootCount   = extraLootCount;
            // extraLootTable / corpsePrefab은 Inspector에서 수동 할당 (SO 자산이 아직 없을 수 있음)

            // 참조 경고
            if (so.defaultWeapon == null)
                Debug.LogWarning($"[SOGen] {file}: Weapon '{weaponFile}' 못 찾음 — Weapons를 먼저 생성하세요");
            if (so.defaultAmmo == null)
                Debug.LogWarning($"[SOGen] {file}: Ammo '{ammoFile}' 못 찾음 — Ammo를 먼저 생성하세요");

            AssetDatabase.CreateAsset(so, path);
            return 1;
        }

        // ══════════════════════════════════════════
        //  QUEST DATA (8종) — GameSystem §5 v2.1
        // ══════════════════════════════════════════

        static int GenerateQuests()
        {
            string folder = "Assets/_Project/Data/Quests";
            EnsureFolder(folder);
            int count = 0;

            // ── Q1: 민간 지역 적 처치 ──
            count += CreateQuest(folder, "Q1_CivilianClear", "Q1", "민간 지역 소탕",
                "민간 지역에 있는 적 10명을 처치하십시오.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Kill, targetID = "Enemy_Zone1_Any", requiredCount = 10, location = "민간 지역" }
                },
                new QuestReward[]
                {
                    new() { type = RewardType.Item, itemID = "Weapon_Rare_Choice_Q1", amount = 1 }
                },
                "", "", false);

            // ── Q2: 발전소 보스 ──
            count += CreateQuest(folder, "Q2_PowerPlantBoss", "Q2", "발전소 보스 처치",
                "발전소에 있는 보스를 처치하십시오.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Kill, targetID = "Boss_PowerPlant", requiredCount = 1, location = "발전소" }
                },
                null,
                "MapA_Zone2", "Q1", false);

            // ── Q2-1: 숲 보스 (사이드) ──
            count += CreateQuest(folder, "Q2_1_ForestBoss", "Q2-1", "숲의 사냥꾼",
                "[사이드] 숲에 있는 보스를 처치하십시오.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Kill, targetID = "Boss_Forest_Sniper", requiredCount = 1, location = "숲" }
                },
                new QuestReward[]
                {
                    new() { type = RewardType.Item, itemID = "Weapon_SK74", amount = 1 }
                },
                "", "Q1", true);

            // ── Q3: 창고 보스 ──
            count += CreateQuest(folder, "Q3_WarehouseBoss", "Q3", "창고 보스 처치",
                "창고에 있는 보스를 처치하십시오.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Kill, targetID = "Boss_Warehouse", requiredCount = 1, location = "창고" }
                },
                null,
                "MapA_Zone3", "Q2", false);

            // ── Q3-1: 제재소 보스 (사이드) ──
            count += CreateQuest(folder, "Q3_1_SawmillBoss", "Q3-1", "제재소의 수호자",
                "[사이드] 제재소에 있는 보스를 처치하십시오.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Kill, targetID = "Boss_Sawmill", requiredCount = 1, location = "제재소" }
                },
                new QuestReward[]
                {
                    new() { type = RewardType.Item, itemID = "Weapon_MB7", amount = 1 }
                },
                "", "Q2", true);

            // ── Q4: 군사 지역 보스 ──
            count += CreateQuest(folder, "Q4_MilitaryBoss", "Q4", "군사 지역 보스 처치",
                "군사 지역에 있는 보스를 처치하십시오.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Kill, targetID = "Boss_MilitaryBase", requiredCount = 1, location = "군사 지역" }
                },
                new QuestReward[]
                {
                    new() { type = RewardType.Item, itemID = "CrashSite_Key", amount = 1 }
                },
                "", "Q3", false);

            // ── Q5: 스테이지 2 이동 ──
            count += CreateQuest(folder, "Q5_MoveToStage2", "Q5", "스테이지 2 진입",
                "추락 지점을 통해 스테이지 2로 이동하십시오.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Reach, targetID = "CrashSite_TransitionPoint", requiredCount = 1, location = "추락 지점" }
                },
                new QuestReward[]
                {
                    new() { type = RewardType.Item, itemID = "Armor_C6", amount = 1 },
                    new() { type = RewardType.Item, itemID = "Helmet_C4", amount = 1 }
                },
                "MapB_All", "Q4", false);

            // ── Q6: Stage 2 모든 보스 ──
            count += CreateQuest(folder, "Q6_AllBosses", "Q6", "최종 보스 처치",
                "모든 보스를 처치하십시오. 마지막 보스가 보트 열쇠를 드랍합니다.",
                new QuestObjectiveData[]
                {
                    new() { type = ObjectiveType.Kill, targetID = "Boss_Stage2_All", requiredCount = 3, location = "맵B 전체" }
                },
                null, // 보트 열쇠는 BossKeyDropper가 마지막 보스 사체에서 드랍 (QuestReward 아님)
                "", "Q5", false);

            return count;
        }

        static int CreateQuest(string folder, string file, string questID, string questName,
            string description, QuestObjectiveData[] objectives, QuestReward[] rewards,
            string unlockZoneID, string prerequisiteQuestID, bool isSideQuest)
        {
            string path = $"{folder}/{file}.asset";
            if (AssetDatabase.LoadAssetAtPath<QuestDataSO>(path) != null)
            {
                Debug.Log($"[SOGen] SKIP (이미 존재): {path}");
                return 0;
            }

            var so = ScriptableObject.CreateInstance<QuestDataSO>();
            so.questID              = questID;
            so.questName            = questName;
            so.description          = description;
            so.objectives           = objectives ?? new QuestObjectiveData[0];
            so.rewards              = rewards ?? new QuestReward[0];
            so.unlockZoneID         = unlockZoneID ?? "";
            so.prerequisiteQuestID  = prerequisiteQuestID ?? "";
            so.isSideQuest          = isSideQuest;

            AssetDatabase.CreateAsset(so, path);
            return 1;
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