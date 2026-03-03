#import <AVFoundation/AVFoundation.h>

/// Native Objective-C++ bridge for configuring AVAudioSession on iOS.
/// Called from C# via [DllImport("__Internal")] in iOSAudioConfiguration.cs.
///
/// Enables hardware AEC (Acoustic Echo Cancellation) by setting the audio session
/// to .playAndRecord category with .videoChat mode. This matches the LiveKit Swift
/// SDK defaults — VideoChat mode enables AEC and routes audio to speaker.

extern "C" {

    /// Configure AVAudioSession for voice chat with hardware AEC enabled.
    /// Sets category to PlayAndRecord with AllowBluetooth and AllowBluetoothA2DP options,
    /// and mode to VideoChat which enables hardware AEC and speaker output on iOS.
    /// Returns 1 on success, 0 on failure.
    int EstuaryiOS_ConfigureForVoiceChat()
    {
        @autoreleasepool {
            NSLog(@"[iOSAudioBridge] Configuring AVAudioSession for voice chat...");

            AVAudioSession *session = [AVAudioSession sharedInstance];
            NSError *error = nil;

            // Set category to PlayAndRecord with Bluetooth options
            // - AllowBluetooth: allows Bluetooth HFP headsets (which have their own AEC)
            // - AllowBluetoothA2DP: allows high-quality A2DP Bluetooth audio output
            BOOL categorySuccess = [session setCategory:AVAudioSessionCategoryPlayAndRecord
                                            withOptions:(AVAudioSessionCategoryOptionAllowBluetooth |
                                                         AVAudioSessionCategoryOptionAllowBluetoothA2DP |
                                                         AVAudioSessionCategoryOptionDefaultToSpeaker)
                                                  error:&error];
            if (!categorySuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to set audio category: %@", error.localizedDescription);
                return 0;
            }

            // Set mode to VideoChat — enables hardware AEC and routes audio to speaker
            // VideoChat mode applies the same voice processing as VoiceChat:
            // - Acoustic Echo Cancellation (AEC)
            // - Noise Suppression (NS)
            // - Auto Gain Control (AGC)
            // but defaults to speaker output instead of earpiece (no DefaultToSpeaker needed)
            error = nil;
            BOOL modeSuccess = [session setMode:AVAudioSessionModeVideoChat error:&error];
            if (!modeSuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to set video chat mode: %@", error.localizedDescription);
                return 0;
            }

            // Activate the audio session
            error = nil;
            BOOL activateSuccess = [session setActive:YES error:&error];
            if (!activateSuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to activate audio session: %@", error.localizedDescription);
                return 0;
            }

            NSLog(@"[iOSAudioBridge] AVAudioSession configured successfully:");
            NSLog(@"[iOSAudioBridge]   Category: PlayAndRecord");
            NSLog(@"[iOSAudioBridge]   Mode: VideoChat (hardware AEC enabled)");
            NSLog(@"[iOSAudioBridge]   Sample Rate: %.0f Hz", session.sampleRate);
            NSLog(@"[iOSAudioBridge]   Input Channels: %ld", (long)session.inputNumberOfChannels);
            NSLog(@"[iOSAudioBridge]   Output Channels: %ld", (long)session.outputNumberOfChannels);

            return 1;
        }
    }

    /// Reset AVAudioSession to Unity's default configuration.
    /// Sets category back to Playback and mode to Default.
    /// Returns 1 on success, 0 on failure.
    int EstuaryiOS_ResetAudioConfiguration()
    {
        @autoreleasepool {
            NSLog(@"[iOSAudioBridge] Resetting AVAudioSession to default...");

            AVAudioSession *session = [AVAudioSession sharedInstance];
            NSError *error = nil;

            // Reset mode to Default first
            BOOL modeSuccess = [session setMode:AVAudioSessionModeDefault error:&error];
            if (!modeSuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to reset audio mode: %@", error.localizedDescription);
                return 0;
            }

            // Reset category to Playback (Unity's default)
            error = nil;
            BOOL categorySuccess = [session setCategory:AVAudioSessionCategoryPlayback error:&error];
            if (!categorySuccess)
            {
                NSLog(@"[iOSAudioBridge] Failed to reset audio category: %@", error.localizedDescription);
                return 0;
            }

            NSLog(@"[iOSAudioBridge] AVAudioSession reset to default (Playback/Default)");
            return 1;
        }
    }

    /// Check if AEC is available. On iOS, hardware AEC is always available
    /// when using VideoChat mode, so this always returns 1.
    int EstuaryiOS_IsAecAvailable()
    {
        // Hardware AEC is always available on iOS when using VideoChat mode.
        // Unlike Android where AEC depends on device hardware/drivers,
        // iOS guarantees AEC in the VideoChat audio session mode.
        return 1;
    }
}
