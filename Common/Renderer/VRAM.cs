﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

namespace PSXPrev.Common.Renderer
{
    public class VRAM : IReadOnlyList<Texture>, IDisposable
    {
        public const int PageCount = 32;
        public const int PageSize = 256;
        private const int PageSemiTransparencyX = PageSize;

        public static readonly System.Drawing.Color DefaultBackgroundColor = System.Drawing.Color.White;


        private readonly Scene _scene;
        private readonly Texture[] _vramPages;
        private readonly bool[] _modifiedPages; // Pages that that require a scene update.
        private readonly bool[] _usedPages; // Pages that have textures drawn to them (not reset unless cleared).

        public System.Drawing.Color BackgroundColor { get; set; } = DefaultBackgroundColor;

        public VRAM(Scene scene)
        {
            _scene = scene;
            _vramPages = new Texture[PageCount];
            _modifiedPages = new bool[PageCount];
            _usedPages = new bool[PageCount];
        }

        public Texture this[uint index] => _vramPages[index];
        public Texture this[int index] => _vramPages[index];

        public int Count => PageCount;

        public IEnumerator<Texture> GetEnumerator()
        {
            return ((IReadOnlyList<Texture>)_vramPages).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            for (var i = 0; i < PageCount; i++)
            {
                _vramPages[i]?.Dispose();
                //_vramPages[i] = null;
                _modifiedPages[i] = false;
                _usedPages[i] = false;
            }
        }

        public void Setup(bool suppressUpdate = false)
        {
            for (var i = 0; i < PageCount; i++)
            {
                if (_vramPages[i] == null)
                {
                    // X coordinates [0,256) store texture data.
                    // X coordinates [256,512) store semi-transparency information for textures.
                    _vramPages[i] = new Texture(PageSize * 2, PageSize, 0, 0, 32, i, true); // Is VRAM page
                    _vramPages[i].TextureName = $"VRAM[{i}]";
                    ClearPage(i, suppressUpdate);
                }
            }
        }

        // Gets if a page has had at least one texture drawn to it.
        public bool IsPageUsed(uint index) => IsPageUsed((int)index);

        public bool IsPageUsed(int index)
        {
            return _usedPages[index];
        }

        // Returns true if the index is a valid VRAM texture page number.
        public bool ContainsPage(uint index) => ContainsPage((int)index);

        public bool ContainsPage(int index)
        {
            return index >= 0 && index < PageCount;
        }

        // Update page textures in the scene.
        public void UpdatePage(uint index, bool force = false) => UpdatePage((int)index, force);

        public void UpdatePage(int index, bool force = false)
        {
            if (force || _modifiedPages[index])
            {
                _scene.UpdateTexture(_vramPages[index].Bitmap, index);
                _modifiedPages[index] = false;
            }
        }

        public void UpdateAllPages()
        {
            for (var i = 0; i < PageCount; i++)
            {
                UpdatePage(i, false); // Only update page if modified.
            }
        }

        // Clear page textures to background color.
        public void ClearPage(uint index, bool suppressUpdate = false) => ClearPage((int)index, suppressUpdate);

        public void ClearPage(int index, bool suppressUpdate = false)
        {
            using (var graphics = Graphics.FromImage(_vramPages[index].Bitmap))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                // Clear texture data to background color.
                graphics.Clear(BackgroundColor);

                // Clear semi-transparent information to its default.
                using (var brush = new SolidBrush(Texture.NoSemiTransparentFlag))
                {
                    graphics.FillRectangle(brush, PageSemiTransparencyX, 0, PageSize, PageSize);
                }
            }

            _usedPages[index] = false;
            if (suppressUpdate)
            {
                _modifiedPages[index] = true;
            }
            else
            {
                UpdatePage(index, true);
            }
        }

        public void ClearAllPages()
        {
            for (var i = 0; i < PageCount; i++)
            {
                ClearPage(i);
            }
        }

        // Draw texture onto page.
        public void DrawTexture(Texture texture, bool suppressUpdate = false)
        {
            var index = texture.TexturePage;
            var textureX = texture.X;
            var textureY = texture.Y;
            var textureWidth = texture.Width;
            var textureHeight = texture.Height;
            var textureBitmap = texture.Bitmap;
            var textureSemiTransparentMap = texture.SemiTransparentMap;
            using (var graphics = Graphics.FromImage(_vramPages[index].Bitmap))
            {
                // Use SourceCopy to overwrite image alpha with alpha stored in textures.
                graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                // Draw the actual texture to VRAM.
                // Clip drawing region so we don't draw over semi-transparent information.
                graphics.SetClip(new Rectangle(0, 0, PageSize, PageSize));
                graphics.DrawImage(textureBitmap, textureX, textureY, textureWidth, textureHeight);

                // Draw semi-transparent information to VRAM in X coordinates [256,512).
                graphics.SetClip(new Rectangle(PageSemiTransparencyX, 0, PageSize, PageSize));
                if (textureSemiTransparentMap != null)
                {
                    graphics.DrawImage(textureSemiTransparentMap, PageSemiTransparencyX + textureX, textureY, textureWidth, textureHeight);
                }
                else
                {
                    using (var brush = new SolidBrush(Texture.NoSemiTransparentFlag))
                    {
                        graphics.FillRectangle(brush, PageSemiTransparencyX + textureX, textureY, textureWidth, textureHeight);
                    }
                }
                graphics.ResetClip();
            }

            _usedPages[index] = true;
            if (suppressUpdate)
            {
                _modifiedPages[index] = true;
            }
            else
            {
                UpdatePage(index, true);
            }
        }


        // Returns a bitmap of the VRAM texture page without the semi-transparency section.
        // Must dispose of Bitmap after use.
        public static Bitmap ConvertTexture(Texture texture, bool semiTransparency)
        {
            var stpX = semiTransparency ? PageSemiTransparencyX : 0;
            var srcRect = new Rectangle(stpX, 0, PageSize, PageSize);

            var bitmap = new Bitmap(PageSize, PageSize);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                    graphics.DrawImage(texture.Bitmap, 0, 0, srcRect, GraphicsUnit.Pixel);
                }
                return bitmap;
            }
            catch
            {
                bitmap?.Dispose();
                throw;
            }
        }

        public static Bitmap ConvertTiledTexture(Texture texture, Rectangle srcRect, int repeatX, int repeatY, bool semiTransparency)
        {
            var stpX = semiTransparency ? PageSemiTransparencyX : 0;
            srcRect.X += stpX;

            var bitmap = new Bitmap(repeatX * srcRect.Width, repeatY * srcRect.Height);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                    for (var ry = 0; ry < repeatY; ry++)
                    {
                        for (var rx = 0; rx < repeatX; rx++)
                        {
                            var x = rx * srcRect.Width;
                            var y = ry * srcRect.Height;
                            graphics.DrawImage(texture.Bitmap, x, y, srcRect, GraphicsUnit.Pixel);
                        }
                    }
                }
                return bitmap;
            }
            catch
            {
                bitmap?.Dispose();
                throw;
            }
        }

        // Draw individual textures into 256x256 cells.
        public static Bitmap ConvertSingleTexture(IEnumerable<Texture> textures, int countX, int countY, bool semiTransparency)
        {
            var packedTextures = new List<Texture[]>();
            foreach (var texture in textures)
            {
                packedTextures.Add(new Texture[1] { texture });
            }
            return ConvertSingleTexture(packedTextures, countX, countY, semiTransparency);
        }

        // Pack textures into 256x256 cells. The inner enumerable of textures will all be drawn to the same cell,
        // and the Texture X,Y determines where in the cell that texture is drawn.
        public static Bitmap ConvertSingleTexture(IEnumerable<IEnumerable<Texture>> packedTextures, int countX, int countY, bool semiTransparency)
        {
            var bitmap = new Bitmap(countX * PageSize, countY * PageSize);
            try
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

                    // Clear the image, because there may be cells that we don't fill in.
                    if (!semiTransparency)
                    {
                        graphics.Clear(DefaultBackgroundColor);
                    }
                    else
                    {
                        graphics.Clear(Texture.NoSemiTransparentFlag);
                    }

                    var i = 0;
                    foreach (var cell in packedTextures)
                    {
                        var x = (i % countX) * PageSize;
                        var y = (i / countX) * PageSize;
                        i++;

                        foreach (var texture in cell)
                        {
                            graphics.SetClip(new Rectangle(x, y, PageSize, PageSize));
                            if (texture.IsVRAMPage)
                            {
                                var stpX = semiTransparency ? PageSemiTransparencyX : 0;
                                var srcRect = new Rectangle(stpX, 0, PageSize, PageSize);
                                graphics.DrawImage(texture.Bitmap, x, y, srcRect, GraphicsUnit.Pixel);
                            }
                            else
                            {
                                var textureBitmap = semiTransparency ? texture.SemiTransparentMap : texture.Bitmap;
                                // Texture may not have semi-transparent map
                                if (textureBitmap != null)
                                {
                                    graphics.DrawImage(textureBitmap, x + texture.X, y + texture.Y);
                                    // Packed boundary debugging:
                                    //graphics.DrawRectangle(Pens.Red, new Rectangle(x + texture.X, y + texture.Y, texture.Width, texture.Height));
                                }
                            }
                        }
                    }
                }
                return bitmap;
            }
            catch
            {
                bitmap?.Dispose();
                throw;
            }
        }

        public static uint ClampTexturePage(uint index) => (uint)ClampTexturePage((int)index);

        public static int ClampTexturePage(int index)
        {
            return Math.Max(0, Math.Min(PageCount - 1, index));
        }

        public static int ClampTextureX(int x)
        {
            return Math.Max(0, Math.Min(PageSize - 1, x));
        }

        public static int ClampTextureY(int y)
        {
            return Math.Max(0, Math.Min(PageSize - 1, y));
        }
    }
}