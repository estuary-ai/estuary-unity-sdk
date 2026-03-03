#import <AudioToolbox/AudioToolbox.h>
#import <AVFoundation/AVFoundation.h>

/// Native iOS plugin that captures microphone audio through the Voice Processing I/O (VPIO) audio unit.
///
/// VPIO is the ONLY way to get hardware AEC on iOS. Unity's Microphone.Start() uses RemoteIO,
/// which does NOT apply AEC even when AVAudioSession is in VideoChat mode. The AEC is coupled
/// to the VPIO audio unit, not just the session mode.
///
/// Audio flow:
///   VPIO audio unit (hardware AEC + NS + AGC) → render callback → ring buffer → C# polling
///
/// Thread safety: The render callback runs on a real-time audio thread. We use a lock-free
/// single-producer single-consumer ring buffer to pass data to the Unity main thread.

// Ring buffer size: ~1 second of 48kHz mono audio (generous headroom)
#define RING_BUFFER_SIZE (48000 * 2)

// VPIO capture state
static AudioUnit _vpioUnit = NULL;
static bool _isCapturing = false;

// Lock-free ring buffer (SPSC: audio thread writes, Unity thread reads)
static float _ringBuffer[RING_BUFFER_SIZE];
static volatile int32_t _writeIndex = 0;
static volatile int32_t _readIndex = 0;

// Target format — sample rate is set dynamically after session activation
static Float64 _actualSampleRate = 48000.0;
static const UInt32 kTargetChannels = 1;

#pragma mark - Ring Buffer Operations

static inline int32_t RingBuffer_AvailableToRead()
{
    int32_t w = _writeIndex;
    int32_t r = _readIndex;
    if (w >= r)
        return w - r;
    else
        return RING_BUFFER_SIZE - r + w;
}

static inline int32_t RingBuffer_AvailableToWrite()
{
    // Leave one slot empty to distinguish full from empty
    return RING_BUFFER_SIZE - 1 - RingBuffer_AvailableToRead();
}

static void RingBuffer_Write(const float *data, int32_t count)
{
    int32_t available = RingBuffer_AvailableToWrite();
    if (count > available)
    {
        // Drop oldest data by advancing read index
        int32_t overflow = count - available;
        _readIndex = (_readIndex + overflow) % RING_BUFFER_SIZE;
    }

    int32_t w = _writeIndex;
    int32_t firstPart = RING_BUFFER_SIZE - w;

    if (count <= firstPart)
    {
        memcpy(&_ringBuffer[w], data, count * sizeof(float));
    }
    else
    {
        memcpy(&_ringBuffer[w], data, firstPart * sizeof(float));
        memcpy(&_ringBuffer[0], data + firstPart, (count - firstPart) * sizeof(float));
    }

    _writeIndex = (w + count) % RING_BUFFER_SIZE;
}

static int32_t RingBuffer_Read(float *output, int32_t maxCount)
{
    int32_t available = RingBuffer_AvailableToRead();
    int32_t toRead = (maxCount < available) ? maxCount : available;
    if (toRead <= 0) return 0;

    int32_t r = _readIndex;
    int32_t firstPart = RING_BUFFER_SIZE - r;

    if (toRead <= firstPart)
    {
        memcpy(output, &_ringBuffer[r], toRead * sizeof(float));
    }
    else
    {
        memcpy(output, &_ringBuffer[r], firstPart * sizeof(float));
        memcpy(output + firstPart, &_ringBuffer[0], (toRead - firstPart) * sizeof(float));
    }

    _readIndex = (r + toRead) % RING_BUFFER_SIZE;
    return toRead;
}

#pragma mark - VPIO Render Callback

static OSStatus VPIOInputCallback(
    void *inRefCon,
    AudioUnitRenderActionFlags *ioActionFlags,
    const AudioTimeStamp *inTimeStamp,
    UInt32 inBusNumber,
    UInt32 inNumberFrames,
    AudioBufferList *ioData)
{
    // Allocate a buffer list for the input data
    AudioBufferList bufferList;
    bufferList.mNumberBuffers = 1;
    bufferList.mBuffers[0].mDataByteSize = inNumberFrames * sizeof(float);
    bufferList.mBuffers[0].mNumberChannels = kTargetChannels;

    // Use a stack-allocated buffer for small frames, heap for large
    float stackBuffer[1024];
    float *audioData;
    bool heapAllocated = false;

    if (inNumberFrames <= 1024)
    {
        audioData = stackBuffer;
    }
    else
    {
        audioData = (float *)malloc(inNumberFrames * sizeof(float));
        heapAllocated = true;
    }

    bufferList.mBuffers[0].mData = audioData;

    // Render the input (this is where VPIO applies AEC, NS, AGC)
    OSStatus status = AudioUnitRender(_vpioUnit, ioActionFlags, inTimeStamp,
                                       1, // Input bus (bus 1 = mic input)
                                       inNumberFrames, &bufferList);

    if (status == noErr)
    {
        // Write AEC-processed audio to ring buffer
        RingBuffer_Write(audioData, (int32_t)inNumberFrames);
    }

    if (heapAllocated)
    {
        free(audioData);
    }

    return status;
}

#pragma mark - C Interface

extern "C" {

    /// Start VPIO audio capture. Configures AVAudioSession, creates the VPIO audio unit,
    /// and begins capturing AEC-processed audio at the native hardware sample rate.
    /// Returns 1 on success, 0 on failure.
    int EstuaryVPIO_Start()
    {
        @autoreleasepool {
            if (_isCapturing)
            {
                NSLog(@"[VPIOAudioCapture] Already capturing");
                return 1;
            }

            NSLog(@"[VPIOAudioCapture] Starting VPIO audio capture...");

            // Reset ring buffer
            _writeIndex = 0;
            _readIndex = 0;
            memset(_ringBuffer, 0, sizeof(_ringBuffer));

            // --- Configure AVAudioSession BEFORE creating VPIO unit ---
            AVAudioSession *session = [AVAudioSession sharedInstance];
            NSError *error = nil;

            BOOL categorySuccess = [session setCategory:AVAudioSessionCategoryPlayAndRecord
                                            withOptions:(AVAudioSessionCategoryOptionAllowBluetooth |
                                                         AVAudioSessionCategoryOptionAllowBluetoothA2DP |
                                                         AVAudioSessionCategoryOptionDefaultToSpeaker)
                                                  error:&error];
            if (!categorySuccess)
            {
                NSLog(@"[VPIOAudioCapture] Failed to set audio category: %@", error.localizedDescription);
                return 0;
            }

            error = nil;
            BOOL modeSuccess = [session setMode:AVAudioSessionModeVideoChat error:&error];
            if (!modeSuccess)
            {
                NSLog(@"[VPIOAudioCapture] Failed to set video chat mode: %@", error.localizedDescription);
                return 0;
            }

            // Don't request a preferred sample rate — use the native hardware rate.
            // Forcing 16kHz caused garbled audio when iOS delivers at 48kHz regardless.

            error = nil;
            BOOL activateSuccess = [session setActive:YES error:&error];
            if (!activateSuccess)
            {
                NSLog(@"[VPIOAudioCapture] Failed to activate audio session: %@", error.localizedDescription);
                return 0;
            }

            _actualSampleRate = session.sampleRate;
            NSLog(@"[VPIOAudioCapture] AVAudioSession configured: rate=%.0fHz, mode=VideoChat", _actualSampleRate);

            // --- Create VPIO audio unit ---
            AudioComponentDescription desc = {
                .componentType = kAudioUnitType_Output,
                .componentSubType = kAudioUnitSubType_VoiceProcessingIO,
                .componentManufacturer = kAudioUnitManufacturer_Apple,
                .componentFlags = 0,
                .componentFlagsMask = 0
            };

            AudioComponent component = AudioComponentFindNext(NULL, &desc);
            if (component == NULL)
            {
                NSLog(@"[VPIOAudioCapture] Failed to find VPIO audio component");
                return 0;
            }

            OSStatus status = AudioComponentInstanceNew(component, &_vpioUnit);
            if (status != noErr)
            {
                NSLog(@"[VPIOAudioCapture] Failed to create VPIO instance: %d", (int)status);
                return 0;
            }

            // Enable input (mic) on bus 1
            UInt32 enableInput = 1;
            status = AudioUnitSetProperty(_vpioUnit,
                                          kAudioOutputUnitProperty_EnableIO,
                                          kAudioUnitScope_Input,
                                          1, // Bus 1 = input (mic)
                                          &enableInput,
                                          sizeof(enableInput));
            if (status != noErr)
            {
                NSLog(@"[VPIOAudioCapture] Failed to enable input: %d", (int)status);
                AudioComponentInstanceDispose(_vpioUnit);
                _vpioUnit = NULL;
                return 0;
            }

            // Set output format on input bus (what we receive from the mic after AEC)
            AudioStreamBasicDescription format = {0};
            format.mSampleRate = _actualSampleRate;
            format.mFormatID = kAudioFormatLinearPCM;
            format.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked | kAudioFormatFlagIsNonInterleaved;
            format.mBytesPerPacket = sizeof(float);
            format.mFramesPerPacket = 1;
            format.mBytesPerFrame = sizeof(float);
            format.mChannelsPerFrame = kTargetChannels;
            format.mBitsPerChannel = 32;

            status = AudioUnitSetProperty(_vpioUnit,
                                          kAudioUnitProperty_StreamFormat,
                                          kAudioUnitScope_Output,
                                          1, // Bus 1 output scope = what we read from mic
                                          &format,
                                          sizeof(format));
            if (status != noErr)
            {
                NSLog(@"[VPIOAudioCapture] Failed to set input format: %d", (int)status);
                AudioComponentInstanceDispose(_vpioUnit);
                _vpioUnit = NULL;
                return 0;
            }

            // Set the render callback on bus 0 (output bus) — this triggers our input rendering
            AURenderCallbackStruct callbackStruct = {
                .inputProc = VPIOInputCallback,
                .inputProcRefCon = NULL
            };

            status = AudioUnitSetProperty(_vpioUnit,
                                          kAudioUnitProperty_SetRenderCallback,
                                          kAudioUnitScope_Input,
                                          0, // Bus 0 = output
                                          &callbackStruct,
                                          sizeof(callbackStruct));
            if (status != noErr)
            {
                NSLog(@"[VPIOAudioCapture] Failed to set render callback: %d", (int)status);
                AudioComponentInstanceDispose(_vpioUnit);
                _vpioUnit = NULL;
                return 0;
            }

            // Set output format on bus 0 as well (silence output — we only want input)
            status = AudioUnitSetProperty(_vpioUnit,
                                          kAudioUnitProperty_StreamFormat,
                                          kAudioUnitScope_Input,
                                          0, // Bus 0 input scope = what goes to speaker
                                          &format,
                                          sizeof(format));
            if (status != noErr)
            {
                NSLog(@"[VPIOAudioCapture] Warning: Failed to set output format: %d (non-fatal)", (int)status);
            }

            // Initialize and start
            status = AudioUnitInitialize(_vpioUnit);
            if (status != noErr)
            {
                NSLog(@"[VPIOAudioCapture] Failed to initialize VPIO: %d", (int)status);
                AudioComponentInstanceDispose(_vpioUnit);
                _vpioUnit = NULL;
                return 0;
            }

            // Verify actual stream format after initialization
            AudioStreamBasicDescription actualFormat = {0};
            UInt32 formatSize = sizeof(actualFormat);
            status = AudioUnitGetProperty(_vpioUnit,
                                          kAudioUnitProperty_StreamFormat,
                                          kAudioUnitScope_Output,
                                          1, // Bus 1 output scope = mic data
                                          &actualFormat,
                                          &formatSize);
            if (status == noErr)
            {
                NSLog(@"[VPIOAudioCapture] Confirmed stream format: %.0fHz, %u ch",
                      actualFormat.mSampleRate, (unsigned int)actualFormat.mChannelsPerFrame);
                // Update actual rate in case VPIO negotiated a different rate
                if (actualFormat.mSampleRate != _actualSampleRate)
                {
                    NSLog(@"[VPIOAudioCapture] Rate adjusted by VPIO: %.0f -> %.0f",
                          _actualSampleRate, actualFormat.mSampleRate);
                    _actualSampleRate = actualFormat.mSampleRate;
                }
            }

            status = AudioOutputUnitStart(_vpioUnit);
            if (status != noErr)
            {
                NSLog(@"[VPIOAudioCapture] Failed to start VPIO: %d", (int)status);
                AudioUnitUninitialize(_vpioUnit);
                AudioComponentInstanceDispose(_vpioUnit);
                _vpioUnit = NULL;
                return 0;
            }

            _isCapturing = true;
            NSLog(@"[VPIOAudioCapture] VPIO capture started successfully (%.0fHz, %u ch, hardware AEC active)",
                  _actualSampleRate, (unsigned int)kTargetChannels);
            return 1;
        }
    }

    /// Stop VPIO audio capture and clean up.
    void EstuaryVPIO_Stop()
    {
        @autoreleasepool {
            if (!_isCapturing)
            {
                NSLog(@"[VPIOAudioCapture] Not capturing, nothing to stop");
                return;
            }

            NSLog(@"[VPIOAudioCapture] Stopping VPIO capture...");

            if (_vpioUnit != NULL)
            {
                AudioOutputUnitStop(_vpioUnit);
                AudioUnitUninitialize(_vpioUnit);
                AudioComponentInstanceDispose(_vpioUnit);
                _vpioUnit = NULL;
            }

            _isCapturing = false;

            // Reset ring buffer
            _writeIndex = 0;
            _readIndex = 0;

            NSLog(@"[VPIOAudioCapture] VPIO capture stopped");
        }
    }

    /// Read available audio samples from the ring buffer.
    /// Returns the number of samples actually read (may be less than maxSamples).
    /// Audio data is 32-bit float, native hardware rate mono, AEC-processed.
    int EstuaryVPIO_ReadAudioData(float *outputBuffer, int maxSamples)
    {
        if (!_isCapturing || outputBuffer == NULL || maxSamples <= 0)
            return 0;

        return RingBuffer_Read(outputBuffer, maxSamples);
    }

    /// Check if VPIO is currently capturing.
    int EstuaryVPIO_IsCapturing()
    {
        return _isCapturing ? 1 : 0;
    }

    /// Get the actual sample rate being used by VPIO (typically 48000).
    /// Call after EstuaryVPIO_Start() to get the hardware-negotiated rate.
    int EstuaryVPIO_GetSampleRate()
    {
        return (int)_actualSampleRate;
    }

    /// Get the number of audio samples available to read.
    int EstuaryVPIO_AvailableSamples()
    {
        if (!_isCapturing) return 0;
        return RingBuffer_AvailableToRead();
    }
}
