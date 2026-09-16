globalThis.mesFiles = {
  async download(fileName, contentType, streamReference) {
    const bytes = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
  },

  async open(fileName, contentType, streamReference) {
    const bytes = await streamReference.arrayBuffer();
    const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
    const opened = window.open(url, '_blank', 'noopener,noreferrer');
    if (!opened) {
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = fileName;
      anchor.click();
      anchor.remove();
    }
    setTimeout(() => URL.revokeObjectURL(url), 60000);
  }
};
