using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Real-money purchases (Unity In-App Purchasing 5), wrapped so the rest of
/// the game never touches the SDK. Built the same way as AdManager: one
/// object that lives for the whole session, started from GameManager.Awake.
///
/// Prices: every product comes back from the App Store / Google Play with its
/// price already written in the player's own currency, for the country their
/// store account is in (see <see cref="PriceFor"/>). The shop shows that, so
/// what the button says is exactly what the store will charge.
///
/// Granting: a purchase is handed to <see cref="GrantHandler"/> (the game adds
/// the tires) and only then confirmed with the store. A purchase that was paid
/// for but never confirmed - the app closed mid-purchase - is offered again on
/// the next launch, and granted then.
/// </summary>
public class IapStore : MonoBehaviour
{
    /// <summary>Tire packs, in shop order. The ids are the App Store Connect / Google Play product ids.</summary>
    public static readonly string[] TirePackIds =
    {
        "com.driftlineeternal.game.tires10",
        "com.driftlineeternal.game.tires55",
        "com.driftlineeternal.game.tires120",
        "com.driftlineeternal.game.tires325",
        "com.driftlineeternal.game.tires700",
        "com.driftlineeternal.game.tires1500",
    };

    /// <summary>Called with a product id once it has been paid for. Set by GameManager.</summary>
    public static Action<string> GrantHandler
    {
        get { return grantHandler; }
        set
        {
            grantHandler = value;
            // anything bought before the game was ready to hand it out
            if (grantHandler == null) return;
            while (waitingGrants.Count > 0)
            {
                var order = waitingGrants[0];
                waitingGrants.RemoveAt(0);
                if (instance != null) instance.Grant(order);
            }
        }
    }

    /// <summary>Raised when the store's prices arrive, so the shop can show them.</summary>
    public static event Action PricesChanged;

    /// <summary>True once the store has answered with the products.</summary>
    public static bool Ready { get { return instance != null && instance.products.Count > 0; } }

    static IapStore instance;
    static Action<string> grantHandler;
    static readonly List<PendingOrder> waitingGrants = new List<PendingOrder>();

    StoreController store;
    readonly Dictionary<string, Product> products = new Dictionary<string, Product>();

    public static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("IapStore");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<IapStore>();
    }

    /// <summary>
    /// The price to show for a product: the store's own, in the player's
    /// currency, or <paramref name="fallback"/> until the store has answered
    /// (offline, or still loading).
    /// </summary>
    public static string PriceFor(string productId, string fallback)
    {
#if UNITY_EDITOR
        // the editor's stand-in store says "$0.01" for everything - show the
        // game's own prices there; real phones get the real store's prices
        return fallback;
#else
        if (instance == null) return fallback;
        Product p;
        if (!instance.products.TryGetValue(productId, out p) || p == null || p.metadata == null)
            return fallback;
        string shown = p.metadata.localizedPriceString;
        return string.IsNullOrEmpty(shown) ? fallback : shown;
#endif
    }

    /// <summary>Starts buying a product. The store shows its own confirmation sheet.</summary>
    public static void Buy(string productId)
    {
        if (instance == null || instance.store == null || !Ready)
        {
            Debug.LogWarning("IapStore: store not ready, cannot buy " + productId);
#if UNITY_EDITOR
            // no store in the editor before it connects - hand it straight over
            // so the shop can still be tried out
            if (grantHandler != null) grantHandler(productId);
#endif
            return;
        }
        instance.store.PurchaseProduct(productId);
    }

    async void Start()
    {
        try
        {
            store = UnityIAPServices.StoreController();
            store.OnProductsFetched += OnProductsFetched;
            store.OnProductsFetchFailed += OnProductsFetchFailed;
            store.OnPurchasePending += OnPurchasePending;
            store.OnPurchaseConfirmed += OnPurchaseConfirmed;
            store.OnPurchaseFailed += OnPurchaseFailed;
            store.OnPurchasesFetched += OnPurchasesFetched;
            store.OnPurchasesFetchFailed += OnPurchasesFetchFailed;
            store.OnStoreConnected += OnStoreConnected;
            store.OnStoreDisconnected += OnStoreDisconnected;

            await store.Connect();

            var wanted = new List<ProductDefinition>();
            foreach (var id in TirePackIds)
                wanted.Add(new ProductDefinition(id, ProductType.Consumable));
            store.FetchProducts(wanted);
        }
        catch (Exception e)
        {
            // no store (no network, no account, unsupported device): the shop
            // keeps its fallback prices and buying just does nothing
            Debug.LogWarning("IapStore: could not connect - " + e.Message);
        }
    }

    void OnProductsFetched(List<Product> fetched)
    {
        foreach (var p in fetched)
        {
            if (p != null && p.definition != null) products[p.definition.id] = p;
        }
        if (PricesChanged != null) PricesChanged();
        // picks up anything paid for but not yet handed out
        store.FetchPurchases();
    }

    void OnProductsFetchFailed(ProductFetchFailed failed)
    {
        Debug.LogWarning("IapStore: products could not be fetched - " + failed);
    }

    void OnPurchasePending(PendingOrder order)
    {
        if (grantHandler == null)
        {
            // the game is not ready to hand it out yet - keep it until it is
            waitingGrants.Add(order);
            return;
        }
        Grant(order);
    }

    void Grant(PendingOrder order)
    {
        foreach (var item in order.CartOrdered.Items())
        {
            if (item == null || item.Product == null || item.Product.definition == null) continue;
            for (int n = 0; n < Mathf.Max(1, item.Quantity); n++)
                grantHandler(item.Product.definition.id);
        }
        // only once the tires are in the save is the store told it is done
        store.ConfirmPurchase(order);
    }

    void OnPurchaseConfirmed(Order order) { }

    void OnPurchaseFailed(FailedOrder order)
    {
        // cancelled, declined or failed - nothing was charged, nothing to do
        Debug.Log("IapStore: purchase did not go through - " + order);
    }

    void OnPurchasesFetched(Orders orders) { }

    void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failed)
    {
        // unfinished purchases will be offered again next time the store connects
        Debug.LogWarning("IapStore: past purchases could not be fetched - " + failed);
    }

    void OnStoreConnected() { }

    void OnStoreDisconnected(StoreConnectionFailureDescription failed)
    {
        // the store reconnects by itself; until then buying just does nothing
        Debug.LogWarning("IapStore: store disconnected - " + failed);
    }
}
