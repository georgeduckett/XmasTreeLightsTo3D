using Microsoft.AspNetCore.SignalR;

namespace TreeLightsWeb.ImageCapture
{
    /// <summary>
    /// The capture page connects here. The server uses it to ask the page (which has the camera) for frames, and to push
    /// the capture's status to every open capture page.
    /// </summary>
    public class ImageCaptureHub : Hub
    {
        private readonly ImageCaptureCoordinator _coordinator;

        public ImageCaptureHub(ImageCaptureCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        /// <summary>
        /// Called by the capture page whenever it (re)connects. <paramref name="clientId"/> stays the same for the browser tab,
        /// even across reloads, so a capture can carry on through a reconnection; <paramref name="pageId"/> changes each time
        /// the page loads.
        /// </summary>
        public CaptureStatus RegisterCamera(string clientId, string pageId)
        {
            if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 100 || string.IsNullOrWhiteSpace(pageId) || pageId.Length > 100)
            {
                throw new HubException("Invalid client id.");
            }
            return _coordinator.RegisterCamera(clientId, pageId, Context.ConnectionId);
        }

        /// <summary>
        /// Called by the capture page when it's given up trying to upload an image.
        /// </summary>
        public void UploadFailed(string jobId, string name, long seq) => _coordinator.UploadFailed(jobId, name, seq);

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _coordinator.ConnectionClosed(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }
    }
}
