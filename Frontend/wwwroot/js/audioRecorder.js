window.audioRecorder = (function () {

    let mediaRecorder;
    let audioChunks = [];

    async function start() {

        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });

        mediaRecorder = new MediaRecorder(stream);

        audioChunks = [];

        mediaRecorder.ondataavailable = e => {
            audioChunks.push(e.data);
        };

        mediaRecorder.start();
    }

    async function stop() {

        return new Promise(resolve => {

            mediaRecorder.onstop = async () => {

                const blob = new Blob(audioChunks, { type: 'audio/wav' });

                const reader = new FileReader();

                reader.onloadend = () => {

                    const base64 = reader.result.split(',')[1];

                    resolve(base64);
                };

                reader.readAsDataURL(blob);
            };

            mediaRecorder.stop();
        });
    }

    return {
        start: start,
        stop: stop
    };

})();