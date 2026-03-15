window.audioRecorder = (function () {

    let mediaRecorder;
    let audioChunks = [];
    let recordedMimeType = '';

    async function start() {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

        // Record in whatever the browser supports — we convert to WAV afterward
        const preferred = [
            'audio/webm;codecs=opus',
            'audio/webm',
            'audio/ogg;codecs=opus',
        ];
        recordedMimeType = preferred.find(t => MediaRecorder.isTypeSupported(t)) || '';

        mediaRecorder = new MediaRecorder(
            stream,
            recordedMimeType ? { mimeType: recordedMimeType } : undefined
        );
        recordedMimeType = mediaRecorder.mimeType;

        audioChunks = [];
        mediaRecorder.ondataavailable = e => { if (e.data.size > 0) audioChunks.push(e.data); };
        mediaRecorder.start();
    }

    async function stop() {
        return new Promise((resolve, reject) => {
            mediaRecorder.onstop = async () => {
                try {
                    const blob = new Blob(audioChunks, { type: recordedMimeType });

                    // Always convert to 16-kHz mono WAV — the only format Azure Speech
                    // accepts reliably across Chrome, Firefox, Edge, and Safari.
                    const wavBlob = await convertToWav(blob);

                    const reader = new FileReader();
                    reader.onloadend = () => {
                        const base64 = reader.result.split(',')[1];
                        // Return plain object — Blazor deserialises this via InvokeAsync<string>
                        resolve(JSON.stringify({ base64: base64, mimeType: 'audio/wav' }));
                    };
                    reader.onerror = () => reject(new Error('FileReader failed'));
                    reader.readAsDataURL(wavBlob);
                } catch (err) {
                    reject(err);
                }
            };

            mediaRecorder.stop();
            mediaRecorder.stream.getTracks().forEach(t => t.stop());
        });
    }

    // ── WAV conversion helpers ────────────────────────────────────────────────

    async function convertToWav(audioBlob) {
        const arrayBuffer = await audioBlob.arrayBuffer();

        // 16 kHz is Azure Speech's sweet spot — avoids resampling on their end
        const audioCtx = new AudioContext({ sampleRate: 16000 });
        let decoded;
        try {
            decoded = await audioCtx.decodeAudioData(arrayBuffer);
        } catch (e) {
            await audioCtx.close();
            throw new Error('Could not decode audio: ' + e.message);
        }

        // Downmix to mono
        const pcm = decoded.numberOfChannels > 1
            ? averageChannels(decoded)
            : decoded.getChannelData(0);

        const wavBuffer = encodeWav(pcm, 16000);
        await audioCtx.close();
        return new Blob([wavBuffer], { type: 'audio/wav' });
    }

    function averageChannels(audioBuffer) {
        const left  = audioBuffer.getChannelData(0);
        const right = audioBuffer.getChannelData(1);
        const mono  = new Float32Array(left.length);
        for (let i = 0; i < left.length; i++) mono[i] = (left[i] + right[i]) / 2;
        return mono;
    }

    function encodeWav(samples, sampleRate) {
        const dataLen = samples.length * 2;          // 16-bit = 2 bytes per sample
        const buffer  = new ArrayBuffer(44 + dataLen);
        const view    = new DataView(buffer);

        function writeStr(offset, str) {
            for (let i = 0; i < str.length; i++)
                view.setUint8(offset + i, str.charCodeAt(i));
        }

        // RIFF header
        writeStr(0,  'RIFF');
        view.setUint32(4,  36 + dataLen, true);      // file size - 8
        writeStr(8,  'WAVE');

        // fmt  chunk
        writeStr(12, 'fmt ');
        view.setUint32(16, 16, true);                // chunk size
        view.setUint16(20,  1, true);                // PCM = 1
        view.setUint16(22,  1, true);                // mono
        view.setUint32(24, sampleRate, true);
        view.setUint32(28, sampleRate * 2, true);    // byte rate (sr * channels * bps/8)
        view.setUint16(32,  2, true);                // block align
        view.setUint16(34, 16, true);                // bits per sample

        // data chunk
        writeStr(36, 'data');
        view.setUint32(40, dataLen, true);

        // PCM samples: clamp float32 → int16
        let offset = 44;
        for (let i = 0; i < samples.length; i++) {
            const s = Math.max(-1, Math.min(1, samples[i]));
            view.setInt16(offset, s < 0 ? s * 0x8000 : s * 0x7FFF, true);
            offset += 2;
        }

        return buffer;
    }

    return { start, stop };

})();