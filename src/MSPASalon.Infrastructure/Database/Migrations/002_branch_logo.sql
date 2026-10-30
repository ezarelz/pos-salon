-- Receipt logo (PNG/JPEG/BMP bytes, max 1 MB). Stored in the database so that backup/restore includes it.
ALTER TABLE branch ADD COLUMN logo BLOB;
