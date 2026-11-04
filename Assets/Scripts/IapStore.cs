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
///
/// Never stuck: if the store cannot be reached or does not send the products
/// (no network yet, a hiccup on the store's side), it keeps trying again on
/// its own, and a tap on a pack while it is not ready tries again at once and
/// tells the caller, so the shop can say so instead of doing nothing.
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

    bool connected;              // the store connection is up
    bool connecting;             // a connect or product fetch is under way
    float retryDelay = FirstRetryDelay;
    const float FirstRetryDelay = 5f;
    const float LongestRetryDelay = 60f;

    // what happened, for the hidden check screen
    static string state = "NOT STARTED";
    static string lastProblem = "-";
    static int buyTaps, buyRefused;

    static string Stamp() { return DateTime.Now.ToString("HH.mm.ss"); }

    public static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("IapStore");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<IapStore>();
    }

    /// <summary>Everything worth knowing about why purchases do or do not work.</summary>
    public static string StatusReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("STORE  ").Append(state).Append('\n');
        int loaded = instance != null ? instance.products.Count : 0;
        sb.Append("PRODUCTS LOADED  ").Append(loaded).Append(" OF ").Append(TirePackIds.Length).Append('\n');
        if (instance != null)
        {
            foreach (var id in TirePackIds)
            {
                if (!instance.products.ContainsKey(id))
                    sb.Append("  MISSING  ").Append(id).Append('\n');
            }
        }
        sb.Append("LAST PROBLEM  ").Append(lastProblem).Append('\n');
        sb.Append("PACK TAPS  ").Append(buyTaps).Append("  REFUSED (NOT READY)  ").Append(buyRefused);
        return sb.ToString();
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

    /// <summary>
    /// Starts buying a product; the store shows its own confirmation sheet.
    /// Returns false when the store is not ready to sell it yet - it is then
    /// already trying again, and the caller should tell the player.
    /// </summary>
    public static bool Buy(string productId)
    {
        buyTaps++;
        Product p = null;
        bool ready = instance != null && instance.store != null && Ready
                     && instance.products.TryGetValue(productId, out p) && p != null;
        if (!ready)
        {
            buyRefused++;
#if UNITY_EDITOR
            // no store in the editor before it connects - hand it straight over
            // so the shop can still be tried out
            if (grantHandler != null)
            {
                grantHandler(productId);
                return true;
            }
#endif
            Debug.LogWarning("IapStore: store not ready, cannot buy " + productId);
            if (instance != null) instance.TryAgainNow();
            return false;
        }
        instance.store.PurchaseProduct(productId);
        return true;
    }

    void Start()
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
        }
        catch (Exception e)
        {
            // no purchasing on this device at all: the shop keeps its fallback
            // prices and a tap on a pack says it is not available
            state = "UNAVAILABLE " + Stamp();
            lastProblem = e.Message;
            Debug.LogWarning("IapStore: could not start - " + e.Message);
            store = null;
            return;
        }
        Connect();
    }

    /// <summary>Connects if need be, then asks the store for the products.</summary>
    async void Connect()
    {
        if (store == null || connecting || Ready) return;
        connecting = true;
        try
        {
            if (!connected)
            {
                state = "CONNECTING " + Stamp();
                await store.Connect();
                connected = true;
            }
            state = "LOADING PRODUCTS " + Stamp();
            var wanted = new List<ProductDefinition>();
            foreach (var id in TirePackIds)
                wanted.Add(new ProductDefinition(id, ProductType.Consumable));
            // the answer arrives in OnProductsFetched / OnProductsFetchFailed
            store.FetchProducts(wanted);
        }
        catch (Exception e)
        {
            connecting = false;
            state = "COULD NOT CONNECT " + Stamp();
            lastProblem = e.Message;
            Debug.LogWarning("IapStore: could not connect - " + e.Message);
            TryAgainLater();
        }
    }

    /// <summary>Another attempt after a pause that grows each time, up to a minute.</summary>
    void TryAgainLater()
    {
        if (Ready) return;
        CancelInvoke(nameof(RetryNow));
        Invoke(nameof(RetryNow), retryDelay);
        retryDelay = Mathf.Min(retryDelay * 2f, LongestRetryDelay);
    }

    /// <summary>A tap on a pack while not ready: try again right away.</summary>
    void TryAgainNow()
    {
        CancelInvoke(nameof(RetryNow));
        retryDelay = FirstRetryDelay;
        RetryNow();
    }

    void RetryNow()
    {
        Connect();
    }

    void OnProductsFetched(List<Product> fetched)
    {
        connecting = false;
        foreach (var p in fetched)
        {
            if (p != null && p.definition != null) products[p.definition.id] = p;
        }
        if (products.Count == 0)
        {
            // connected, but the store sent nothing to sell
            state = "NO PRODUCTS RETURNED " + Stamp();
            TryAgainLater();
            return;
        }
        state = "READY " + Stamp();
        retryDelay = FirstRetryDelay;
        if (PricesChanged != null) PricesChanged();
        // picks up anything paid for but not yet handed out
        store.FetchPurchases();
    }

    void OnProductsFetchFailed(ProductFetchFailed failed)
    {
        connecting = false;
        lastProblem = failed != null ? failed.ToString() : "products could not be fetched";
        Debug.LogWarning("IapStore: products could not be fetched - " + failed);
        if (products.Count == 0)
        {
            state = "PRODUCTS FAILED " + Stamp();
            TryAgainLater();
        }
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
        lastProblem = "purchase did not go through - " + order;
        Debug.Log("IapStore: purchase did not go through - " + order);
    }

    void OnPurchasesFetched(Orders orders) { }

    void OnPurchasesFetchFailed(PurchasesFetchFailureDescription failed)
    {
        // unfinished purchases will be offered again next time the store connects
        Debug.LogWarning("IapStore: past purchases could not be fetched - " + failed);
    }

    void OnStoreConnected()
    {
        connected = true;
    }

    void OnStoreDisconnected(StoreConnectionFailureDescription failed)
    {
        connected = false;
        connecting = false;
        lastProblem = "store disconnected - " + failed;
        Debug.LogWarning("IapStore: store disconnected - " + failed);
        if (!Ready)
        {
            state = "DISCONNECTED " + Stamp();
            TryAgainLater();
        }
    }
}
