using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MapleCoins wallet + owned items + what is equipped per category, saved in PlayerPrefs.
/// QA: <see cref="QaFreePurchases"/> makes every purchase free (prices still shown).
/// </summary>
public static class PlayerWardrobe
{
    /// <summary>QA switch (2026-09-25): everything is free while the shop is being tested.</summary>
    public const bool QaFreePurchases = true;
    private const string Key = "MapleRide.Wardrobe.v1";

    [Serializable] private class Save
    {
        public int coins = 5000;
        public List<string> owned = new List<string>();
        public List<string> equipped = new List<string>();   // one id per category at most
    }

    private static Save _s;
    public static event Action Changed;
    /// <summary>Fired with the amount whenever coins are EARNED (not on purchases), so the
    /// HUD can show a "+25 MC" pop.</summary>
    public static event Action<int> CoinsEarned;

    private static Save S
    {
        get
        {
            if (_s != null) return _s;
            try { _s = JsonUtility.FromJson<Save>(PlayerPrefs.GetString(Key, "")) ?? new Save(); }
            catch { _s = new Save(); }
            return _s;
        }
    }

    public static int Coins => S.coins;
    public static bool Owns(string id) => S.owned.Contains(id);
    public static bool IsEquipped(string id) => S.equipped.Contains(id);

    public static ShopItem Equipped(ShopCategory cat)
    {
        foreach (var id in S.equipped)
        {
            var it = ShopCatalog.Get(id);
            if (it != null && it.Category == cat) return it;
        }
        return null;
    }

    /// <summary>Riding earns MapleCoins (see MapleRowShop.coinsPerKm).</summary>
    public static void AddCoins(int amount)
    {
        if (amount <= 0) return;
        S.coins += amount;
        Persist();
        CoinsEarned?.Invoke(amount);
    }

    public static int PriceToPay(ShopItem item) => QaFreePurchases ? 0 : item.Price;

    /// <summary>Spends coins on something outside the catalog (the race Garage's bike upgrades).
    /// False, and nothing spent, when the wallet is short.</summary>
    public static bool TrySpend(int amount)
    {
        if (amount < 0 || S.coins < amount) return false;
        if (amount == 0) return true;
        S.coins -= amount;
        Persist();
        return true;
    }

    /// <summary>Test-only: drop the cached save so the next read reloads PlayerPrefs.</summary>
    public static void ReloadForTests() { _s = null; }

    public static bool Buy(ShopItem item)
    {
        if (item == null || Owns(item.Id)) return false;
        int cost = PriceToPay(item);
        if (S.coins < cost) return false;
        S.coins -= cost;
        S.owned.Add(item.Id);
        Persist();
        return true;
    }

    /// <summary>Equip (replaces the item of the same category). Equipping an equipped item removes it.</summary>
    public static void ToggleEquip(ShopItem item)
    {
        if (item == null || !Owns(item.Id)) return;
        if (IsEquipped(item.Id)) S.equipped.Remove(item.Id);
        else
        {
            S.equipped.RemoveAll(id => { var o = ShopCatalog.Get(id); return o == null || o.Category == item.Category; });
            S.equipped.Add(item.Id);
        }
        Persist();
    }

    private static void Persist()
    {
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(S));
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
