var tag = document.createElement('script');

tag.src = "https://www.youtube.com/iframe_api";
var firstScriptTag = document.getElementsByTagName('script')[0];
firstScriptTag.parentNode.insertBefore(tag, firstScriptTag);

var player;
function onYouTubeIframeAPIReady() {
    player = new YT.Player('player', {
        height: '390',
        width: '640',
        videoId: 'AgEJK6Vk8Ro',
        events: {
            'onReady': onPlayerReady,
            'onStateChange': onPlayerStateChange
        }
    });
}
function onPlayerReady(event) {
    event.target.playVideo(); // Start playing the video once it's ready
    
}
function onPlayerStateChange(event) {
    if (event.data == YT.PlayerState.PLAYING) {
        // We've started playing, so signal to the server to start the light animation
        $.get('/Home/StartSyncMusic/Six', function (data) { });
    }
}