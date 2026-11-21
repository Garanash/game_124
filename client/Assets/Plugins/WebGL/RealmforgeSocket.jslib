// Binary WebSocket bridge for WebGL builds. Frames are queued in JS and pulled by C# each frame,
// which keeps all game callbacks on Unity's main thread.
var RealmforgeSocket = {
  $sockets: { next: 0, byId: {} },

  RealmforgeSocketOpen: function (urlPointer) {
    var id = sockets.next++;
    var entry = { socket: new WebSocket(UTF8ToString(urlPointer)), frames: [] };
    entry.socket.binaryType = "arraybuffer";
    entry.socket.onmessage = function (event) {
      if (event.data instanceof ArrayBuffer) {
        entry.frames.push(new Uint8Array(event.data));
      }
    };
    sockets.byId[id] = entry;
    return id;
  },

  RealmforgeSocketState: function (id) {
    var entry = sockets.byId[id];
    return entry ? entry.socket.readyState : 3;
  },

  RealmforgeSocketSend: function (id, dataPointer, length) {
    var entry = sockets.byId[id];
    if (entry && entry.socket.readyState === 1) {
      entry.socket.send(HEAPU8.slice(dataPointer, dataPointer + length));
    }
  },

  RealmforgeSocketNextFrameLength: function (id) {
    var entry = sockets.byId[id];
    return entry && entry.frames.length > 0 ? entry.frames[0].length : -1;
  },

  RealmforgeSocketReadFrame: function (id, bufferPointer, length) {
    var entry = sockets.byId[id];
    if (entry && entry.frames.length > 0) {
      HEAPU8.set(entry.frames.shift().subarray(0, length), bufferPointer);
    }
  },

  RealmforgeSocketClose: function (id) {
    var entry = sockets.byId[id];
    if (entry) {
      entry.socket.close();
    }
  },

  RealmforgeSocketRelease: function (id) {
    delete sockets.byId[id];
  },
};

autoAddDeps(RealmforgeSocket, "$sockets");
mergeInto(LibraryManager.library, RealmforgeSocket);
