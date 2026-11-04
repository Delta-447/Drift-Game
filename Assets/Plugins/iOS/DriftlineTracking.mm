// Apple's "allow tracking?" popup (App Tracking Transparency), called from
// TrackingPrompt.cs. iOS 14 and later; older versions have no popup.
#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

// true from asking until iOS answers - so the game never asks twice at once
static volatile bool sTrackingRequestPending = false;

extern "C"
{
    // 0 not asked yet, 1 restricted, 2 denied, 3 allowed
    int _Driftline_TrackingStatus()
    {
        if (@available(iOS 14, *))
            return (int)[ATTrackingManager trackingAuthorizationStatus];
        return 3;
    }

    int _Driftline_TrackingRequestPending()
    {
        return sTrackingRequestPending ? 1 : 0;
    }

    void _Driftline_RequestTracking()
    {
        if (@available(iOS 14, *))
        {
            if (sTrackingRequestPending) return;
            sTrackingRequestPending = true;
            dispatch_async(dispatch_get_main_queue(), ^{
                // if the app was not active, iOS answers at once with "not
                // determined" and shows nothing: TrackingPrompt then asks again
                [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:
                    ^(ATTrackingManagerAuthorizationStatus status) {
                        sTrackingRequestPending = false;
                    }];
            });
        }
    }
}
