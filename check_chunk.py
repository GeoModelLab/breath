import struct

data = open(r'C:\REPO\breath\data\nc files\TX\chunk_0001-0730_1980-1981.bin', 'rb').read(1000*4)
vals = struct.unpack('<1000f', data)

print('First 20 values:')
print([f'{v:.6f}' for v in vals[:20]])

print(f'\nCount of -99.99 values: {sum(1 for v in vals if abs(v + 99.99) < 0.01)}')
print(f'Count of NaN values: {sum(1 for v in vals if v != v)}')
print(f'Valid values (not -99.99, not NaN): {sum(1 for v in vals if v == v and abs(v + 99.99) > 0.01)}')
